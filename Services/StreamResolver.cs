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
        bool IsAudioOnly);

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

        // 1) Prefer single-file progressive format (b) — one URL, works best with LibVLC.
        var single = await RunYtDlpAsync(
            ytdlp,
            [
                "--no-playlist",
                "--no-warnings",
                "-f", "b/best",
                "--print", "%(title)s",
                "-g",
                pageUri.AbsoluteUri
            ],
            cancellationToken).ConfigureAwait(false);

        if (single is { ExitCode: 0 } && single.Lines.Count >= 2)
        {
            var title = single.Lines[0].Trim();
            var url = single.Lines[^1].Trim(); // last line is URL if title had newlines rarely
            // When --print title then -g: stdout is title\nurl
            if (single.Lines.Count == 2)
                url = single.Lines[1].Trim();
            else
            {
                // title may contain newlines; last non-empty line that looks like URL
                url = single.Lines.LastOrDefault(l => l.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                      ?.Trim() ?? url;
                title = single.Lines[0].Trim();
            }

            if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                return new ResolvedStream(
                    pageUri.AbsoluteUri,
                    string.IsNullOrWhiteSpace(title) ? pageUri.Host : title,
                    url,
                    AudioUrl: null,
                    IsAudioOnly: false);
            }
        }

        // 2) Fallback: separate best video + audio (DASH) — use input-slave for audio.
        var multi = await RunYtDlpAsync(
            ytdlp,
            [
                "--no-playlist",
                "--no-warnings",
                "-f", "bv*+ba/b",
                "--print", "%(title)s",
                "-g",
                pageUri.AbsoluteUri
            ],
            cancellationToken).ConfigureAwait(false);

        if (multi is { ExitCode: 0 } && multi.Lines.Count >= 2)
        {
            var lines = multi.Lines.Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            var title = lines.FirstOrDefault(l => !l.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                        ?? pageUri.Host;
            var urls = lines.Where(l => l.StartsWith("http", StringComparison.OrdinalIgnoreCase)).ToList();
            if (urls.Count == 1)
            {
                return new ResolvedStream(pageUri.AbsoluteUri, title, urls[0], null, false);
            }

            if (urls.Count >= 2)
            {
                return new ResolvedStream(
                    pageUri.AbsoluteUri,
                    title,
                    PrimaryUrl: urls[0],
                    AudioUrl: urls[1],
                    IsAudioOnly: false);
            }
        }

        return null;
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
