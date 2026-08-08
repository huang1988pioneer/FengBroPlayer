using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MusicVideoMediaPlayer.Services;

/// <summary>
/// Resolves page URLs (YouTube, etc.) to direct media URLs via yt-dlp when available.
/// LibVLC's built-in youtube.lua is frequently broken; yt-dlp is the reliable path.
/// </summary>
public static class StreamResolver
{
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
        // Explicit override for portable installs.
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

        // PATH search
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

        // Common WinGet location
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
        // Format "b/best" often returns m3u8 which some LibVLC builds fail to open.
        var formatCandidates = new[]
        {
            "best[ext=mp4][protocol^=http][protocol!*=m3u8]/best[ext=mp4][protocol^=http]/18/22/best[ext=mp4]/b",
            "b/best",
            "bv*+ba/b",
        };

        foreach (var format in formatCandidates)
        {
            var result = await RunYtDlpAsync(
                ytdlp,
                [
                    "--no-playlist",
                    "--no-warnings",
                    "-f", format,
                    "--print", "%(title)s",
                    "--print", "%(duration>%H:%M:%S)s",
                    "--print", "%(uploader,channel,creator|)s",
                    "-g",
                    pageUri.AbsoluteUri
                ],
                cancellationToken).ConfigureAwait(false);

            if (result is not { ExitCode: 0 })
                continue;

            var parsed = ParseYtDlpMetaAndUrls(result.Lines, pageUri);
            if (parsed is null)
                continue;

            // Reject meta-only (PrimaryUrl still the page) — keep trying other formats.
            if (string.Equals(parsed.PrimaryUrl, pageUri.AbsoluteUri, StringComparison.OrdinalIgnoreCase))
                continue;

            // First candidate: force single-URL progressive path (ignore accidental dual urls).
            if (format.Contains("18/22", StringComparison.Ordinal) || format.Contains("ext=mp4", StringComparison.Ordinal))
                return parsed with { IsAudioOnly = false, AudioUrl = null };

            return parsed;
        }

        // Metadata-only attempt (title) when stream formats fail — does not enable playback.
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

        var result = await RunYtDlpAsync(
            ytdlp,
            [
                "--no-playlist",
                "--no-warnings",
                "--skip-download",
                "--print", "%(title)s",
                "--print", "%(duration>%H:%M:%S)s",
                "--print", "%(uploader,channel,creator|)s",
                pageUri.AbsoluteUri
            ],
            cancellationToken).ConfigureAwait(false);

        if (result is not { ExitCode: 0 } || result.Lines.Count == 0)
            return null;

        var title = result.Lines.ElementAtOrDefault(0)?.Trim();
        var duration = NormalizeDuration(result.Lines.ElementAtOrDefault(1));
        var uploader = result.Lines.ElementAtOrDefault(2)?.Trim();
        if (string.IsNullOrWhiteSpace(title) || title is "NA" or "None")
            return null;

        return new ResolvedStream(
            pageUri.AbsoluteUri,
            title,
            PrimaryUrl: pageUri.AbsoluteUri,
            AudioUrl: null,
            IsAudioOnly: false,
            Duration: duration,
            Uploader: string.IsNullOrWhiteSpace(uploader) || uploader is "NA" or "None" ? null : uploader);
    }

    private static ResolvedStream? ParseYtDlpMetaAndUrls(IReadOnlyList<string> rawLines, Uri pageUri)
    {
        var lines = rawLines.Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        if (lines.Count == 0)
            return null;

        var urls = lines.Where(l => l.StartsWith("http", StringComparison.OrdinalIgnoreCase)).ToList();
        var meta = lines.Where(l => !l.StartsWith("http", StringComparison.OrdinalIgnoreCase)).ToList();

        var title = meta.ElementAtOrDefault(0);
        var duration = NormalizeDuration(meta.ElementAtOrDefault(1));
        var uploader = meta.ElementAtOrDefault(2);
        if (string.IsNullOrWhiteSpace(title) || title is "NA" or "None")
            title = pageUri.Host;
        if (string.IsNullOrWhiteSpace(uploader) || uploader is "NA" or "None")
            uploader = null;

        if (urls.Count == 0)
        {
            // Meta-only print (no -g lines).
            return new ResolvedStream(
                pageUri.AbsoluteUri, title!, pageUri.AbsoluteUri, null, false, duration, uploader);
        }

        if (urls.Count == 1)
        {
            return new ResolvedStream(
                pageUri.AbsoluteUri, title!, urls[0], null, false, duration, uploader);
        }

        return new ResolvedStream(
            pageUri.AbsoluteUri,
            title!,
            PrimaryUrl: urls[0],
            AudioUrl: urls[1],
            IsAudioOnly: false,
            Duration: duration,
            Uploader: uploader);
    }

    private static string? NormalizeDuration(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw is "NA" or "None" or "0")
            return null;
        raw = raw.Trim();
        // yt-dlp may print H:MM:SS or MM:SS
        if (raw.Count(c => c == ':') == 2 && raw.StartsWith("0:", StringComparison.Ordinal))
            return raw[2..]; // strip leading 0: hours when under 1h → M:SS still ok; keep H:MM:SS if H>0
        if (raw.StartsWith("00:", StringComparison.Ordinal) && raw.Length == 8)
            return raw[3..]; // 00:mm:ss → mm:ss
        return raw;
    }

    private sealed record YtDlpResult(int ExitCode, IReadOnlyList<string> Lines, string StdErr);

    private static async Task<YtDlpResult> RunYtDlpAsync(
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
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        try
        {
            if (!proc.Start())
                return new YtDlpResult(-1, [], "failed to start yt-dlp");
        }
        catch (Exception ex)
        {
            return new YtDlpResult(-1, [], ex.Message);
        }

        var stdoutTask = proc.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = proc.StandardError.ReadToEndAsync(cancellationToken);

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

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        var lines = stdout
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        return new YtDlpResult(proc.ExitCode, lines, stderr);
    }
}
