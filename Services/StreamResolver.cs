using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MusicVideoMediaPlayer.Services;

/// <summary>
/// Resolves page URLs (YouTube, etc.) to direct media URLs via yt-dlp when available.
/// Uses JSON output (-J) so titles stay UTF-8 on Windows (avoids console code-page mojibake).
/// </summary>
public static class StreamResolver
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public sealed record ResolvedStream(
        string PageUrl,
        string Title,
        string PrimaryUrl,
        string? AudioUrl,
        bool IsAudioOnly,
        string? Duration = null,
        string? Uploader = null);

    public static bool NeedsExtraction(string? url)
    {
        if (!MediaEngine.TryNormalizeStreamUri(url, out var uri))
            return false;
        return NeedsExtraction(uri);
    }

    public static bool NeedsExtraction(Uri uri)
    {
        var host = uri.Host;
        return host.Contains("youtube.com", StringComparison.OrdinalIgnoreCase)
               || host.Contains("youtu.be", StringComparison.OrdinalIgnoreCase)
               || host.Contains("youtube-nocookie.com", StringComparison.OrdinalIgnoreCase)
               || host.Contains("music.youtube.com", StringComparison.OrdinalIgnoreCase)
               || host.Contains("twitch.tv", StringComparison.OrdinalIgnoreCase)
               || host.Contains("bilibili.com", StringComparison.OrdinalIgnoreCase)
               || host.Contains("nicovideo.jp", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsYtDlpAvailable() => !string.IsNullOrEmpty(FindYtDlpPath());

    public static string? FindYtDlpPath()
    {
        var env = Environment.GetEnvironmentVariable("YT_DLP_PATH");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env))
            return env;

        var localNames = OperatingSystem.IsWindows()
            ? new[] { "yt-dlp.exe", "yt-dlp" }
            : new[] { "yt-dlp", "youtube-dl" };

        foreach (var name in localNames)
        {
            var besideApp = Path.Combine(AppContext.BaseDirectory, name);
            if (File.Exists(besideApp))
                return besideApp;
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var name in localNames)
            {
                try
                {
                    var candidate = Path.Combine(dir.Trim(), name);
                    if (File.Exists(candidate))
                        return candidate;
                }
                catch
                {
                    // ignore bad PATH entries
                }
            }
        }

        if (OperatingSystem.IsWindows())
        {
            try
            {
                var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var wingetRoot = Path.Combine(local, "Microsoft", "WinGet", "Packages");
                if (Directory.Exists(wingetRoot))
                {
                    var hit = Directory.EnumerateFiles(wingetRoot, "yt-dlp.exe", SearchOption.AllDirectories)
                        .FirstOrDefault();
                    if (hit is not null)
                        return hit;
                }
            }
            catch
            {
                // ignore
            }
        }

        return null;
    }

    /// <summary>
    /// Resolve a page URL to one or two direct stream URLs (video [+ audio]).
    /// Prefers a single progressive format for simplest LibVLC playback.
    /// </summary>
    public static async Task<ResolvedStream?> ResolveAsync(
        string pageUrl,
        CancellationToken cancellationToken = default)
    {
        if (!MediaEngine.TryNormalizeStreamUri(pageUrl, out var pageUri))
            return null;

        var ytdlp = FindYtDlpPath();
        if (ytdlp is null)
            return null;

        // Prefer progressive HTTPS MP4 for LibVLC (single file, no HLS/DASH glue).
        // Bilibili normally serves DASH: its best video stream has no audio.
        // Request an explicit video+audio pair before trying progressive fallbacks.
        var formatCandidates = new[]
        {
            "bv*+ba/b",
            "best[ext=mp4][protocol^=http][protocol!*=m3u8]/best[ext=mp4][protocol^=http]/18/22/best[ext=mp4]/b",
            "b/best",
        };

        foreach (var format in formatCandidates)
        {
            var json = await RunYtDlpJsonAsync(
                ytdlp,
                [
                    "--no-playlist",
                    "--no-warnings",
                    "-f", format,
                    "-J",
                    pageUri.AbsoluteUri
                ],
                cancellationToken).ConfigureAwait(false);

            var parsed = ParseYtDlpJson(json, pageUri, preferSingleUrl: format != "bv*+ba/b");
            if (parsed is null)
                continue;
            if (string.Equals(parsed.PrimaryUrl, pageUri.AbsoluteUri, StringComparison.OrdinalIgnoreCase))
                continue;
            return parsed;
        }

        return await FetchTitleAsync(pageUri.AbsoluteUri, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Lightweight title lookup (no stream URL) for UI labels.</summary>
    public static async Task<ResolvedStream?> FetchTitleAsync(
        string pageUrl,
        CancellationToken cancellationToken = default)
    {
        if (!MediaEngine.TryNormalizeStreamUri(pageUrl, out var pageUri))
            return null;
        var ytdlp = FindYtDlpPath();
        if (ytdlp is null)
            return null;

        var json = await RunYtDlpJsonAsync(
            ytdlp,
            [
                "--no-playlist",
                "--no-warnings",
                "--skip-download",
                "-J",
                pageUri.AbsoluteUri
            ],
            cancellationToken).ConfigureAwait(false);

        return ParseYtDlpJson(json, pageUri, preferSingleUrl: true, metaOnly: true);
    }

    private static ResolvedStream? ParseYtDlpJson(string? json, Uri pageUri, bool preferSingleUrl, bool metaOnly = false)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Playlist dump sometimes wraps entries — take first.
            if (root.TryGetProperty("entries", out var entries)
                && entries.ValueKind == JsonValueKind.Array
                && entries.GetArrayLength() > 0)
            {
                root = entries[0];
            }

            var title = GetString(root, "title")
                        ?? GetString(root, "fulltitle")
                        ?? pageUri.Host;
            var uploader = GetString(root, "uploader")
                           ?? GetString(root, "channel")
                           ?? GetString(root, "creator");
            var duration = FormatDurationSeconds(GetDouble(root, "duration"));

            if (metaOnly)
            {
                return new ResolvedStream(
                    pageUri.AbsoluteUri,
                    title,
                    PrimaryUrl: pageUri.AbsoluteUri,
                    AudioUrl: null,
                    IsAudioOnly: false,
                    Duration: duration,
                    Uploader: uploader);
            }

            // yt-dlp emits a top-level URL even for a DASH video component. Read
            // requested_formats first so that Bilibili's audio URL is retained.
            if (!preferSingleUrl
                && root.TryGetProperty("requested_formats", out var requested)
                && requested.ValueKind == JsonValueKind.Array
                && requested.GetArrayLength() >= 2)
            {
                string? videoUrl = null;
                string? audioUrl = null;
                foreach (var format in requested.EnumerateArray())
                {
                    var url = GetString(format, "url");
                    if (string.IsNullOrWhiteSpace(url)) continue;
                    if (GetString(format, "acodec") is not "none" && GetString(format, "vcodec") is "none")
                        audioUrl ??= url;
                    else if (GetString(format, "vcodec") is not "none")
                        videoUrl ??= url;
                }
                if (!string.IsNullOrWhiteSpace(videoUrl))
                    return new ResolvedStream(pageUri.AbsoluteUri, title, videoUrl, audioUrl, false, duration, uploader);
            }

            // Single progressive / merged format: top-level "url"
            var singleUrl = GetString(root, "url");
            if (!string.IsNullOrWhiteSpace(singleUrl)
                && (preferSingleUrl || !root.TryGetProperty("requested_formats", out _)))
            {
                return new ResolvedStream(
                    pageUri.AbsoluteUri, title, singleUrl!, null, false, duration, uploader);
            }

            // DASH: requested_formats[0]=video, [1]=audio
            if (root.TryGetProperty("requested_formats", out var rf)
                && rf.ValueKind == JsonValueKind.Array
                && rf.GetArrayLength() >= 1)
            {
                var videoUrl = GetString(rf[0], "url");
                string? audioUrl = null;
                if (rf.GetArrayLength() >= 2)
                    audioUrl = GetString(rf[1], "url");

                if (!string.IsNullOrWhiteSpace(videoUrl))
                {
                    if (preferSingleUrl || string.IsNullOrWhiteSpace(audioUrl))
                    {
                        // Try formats list for a progressive alternative if only DASH available.
                        var progressive = FindProgressiveUrl(root);
                        if (!string.IsNullOrWhiteSpace(progressive))
                        {
                            return new ResolvedStream(
                                pageUri.AbsoluteUri, title, progressive!, null, false, duration, uploader);
                        }
                    }

                    return new ResolvedStream(
                        pageUri.AbsoluteUri, title, videoUrl!, audioUrl, false, duration, uploader);
                }
            }

            // Last resort: scan formats array for a progressive http mp4.
            var fromFormats = FindProgressiveUrl(root);
            if (!string.IsNullOrWhiteSpace(fromFormats))
            {
                return new ResolvedStream(
                    pageUri.AbsoluteUri, title, fromFormats!, null, false, duration, uploader);
            }

            if (!string.IsNullOrWhiteSpace(singleUrl))
            {
                return new ResolvedStream(
                    pageUri.AbsoluteUri, title, singleUrl!, null, false, duration, uploader);
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static string? FindProgressiveUrl(JsonElement root)
    {
        if (!root.TryGetProperty("formats", out var formats) || formats.ValueKind != JsonValueKind.Array)
            return null;

        string? best = null;
        var bestScore = -1;
        foreach (var f in formats.EnumerateArray())
        {
            var url = GetString(f, "url");
            if (string.IsNullOrWhiteSpace(url))
                continue;
            var protocol = GetString(f, "protocol") ?? "";
            var ext = GetString(f, "ext") ?? "";
            var vcodec = GetString(f, "vcodec") ?? "none";
            var acodec = GetString(f, "acodec") ?? "none";
            // Progressive = has both video and audio in one URL
            if (vcodec is "none" || acodec is "none")
                continue;
            if (protocol.Contains("m3u8", StringComparison.OrdinalIgnoreCase))
                continue;

            var score = 0;
            if (protocol.StartsWith("http", StringComparison.OrdinalIgnoreCase)) score += 10;
            if (ext.Equals("mp4", StringComparison.OrdinalIgnoreCase)) score += 5;
            var height = GetDouble(f, "height") ?? 0;
            score += (int)Math.Min(height, 1080) / 10;

            if (score > bestScore)
            {
                bestScore = score;
                best = url;
            }
        }

        return best;
    }

    private static string? GetString(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p))
            return null;
        if (p.ValueKind == JsonValueKind.String)
        {
            var s = p.GetString();
            return string.IsNullOrWhiteSpace(s) || s is "NA" or "None" ? null : s;
        }
        if (p.ValueKind is JsonValueKind.Number)
            return p.ToString();
        return null;
    }

    private static double? GetDouble(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p))
            return null;
        if (p.ValueKind == JsonValueKind.Number && p.TryGetDouble(out var d))
            return d;
        if (p.ValueKind == JsonValueKind.String
            && double.TryParse(p.GetString(), out var s))
            return s;
        return null;
    }

    private static string? FormatDurationSeconds(double? seconds)
    {
        if (seconds is null or <= 0 or double.NaN)
            return null;
        var ts = TimeSpan.FromSeconds(seconds.Value);
        return ts.TotalHours >= 1
            ? ts.ToString(@"h\:mm\:ss")
            : ts.ToString(@"mm\:ss");
    }

    private static async Task<string?> RunYtDlpJsonAsync(
        string exe,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            // JSON is UTF-8; also force Python/yt-dlp UTF-8 mode on Windows.
            StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardErrorEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        };

        // Prevent Windows console code-page mojibake for any text yt-dlp writes.
        psi.Environment["PYTHONUTF8"] = "1";
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        psi.Environment["PYTHONLEGACYWINDOWSSTDIO"] = "0";

        foreach (var a in args)
            psi.ArgumentList.Add(a);

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        try
        {
            if (!proc.Start())
                return null;
        }
        catch
        {
            return null;
        }

        // Read as raw bytes then decode as UTF-8 — most reliable on Windows.
        await using var stdoutStream = proc.StandardOutput.BaseStream;
        await using var stderrStream = proc.StandardError.BaseStream;
        var stdoutTask = ReadAllBytesAsync(stdoutStream, cancellationToken);
        var stderrTask = ReadAllBytesAsync(stderrStream, cancellationToken);

        try
        {
            await proc.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!proc.HasExited)
                    proc.Kill(entireProcessTree: true);
            }
            catch { /* ignore */ }

            throw;
        }

        var stdoutBytes = await stdoutTask.ConfigureAwait(false);
        _ = await stderrTask.ConfigureAwait(false);

        if (proc.ExitCode != 0 || stdoutBytes.Length == 0)
            return null;

        // Strip UTF-8 BOM if present.
        var text = Encoding.UTF8.GetString(stdoutBytes).Trim();
        if (text.Length > 0 && text[0] == '\uFEFF')
            text = text[1..];
        return text;
    }

    private static async Task<byte[]> ReadAllBytesAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
        return ms.ToArray();
    }
}
