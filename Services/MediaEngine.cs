using System;
using System.IO;
using Avalonia.Threading;
using LibVLCSharp.Shared;

namespace MusicVideoMediaPlayer.Services;

/// <summary>
/// Thin LibVLC wrapper for audio/video playback with UI-thread callbacks.
/// </summary>
public sealed class MediaEngine : IDisposable
{
    private readonly LibVLC _libVlc;
    private Media? _currentMedia;
    private bool _disposed;

    public MediaPlayer Player { get; }

    public event Action? EndReached;
    public event Action<long, long>? TimeChanged; // timeMs, lengthMs
    public event Action<bool>? PlayingChanged;

    public MediaEngine()
    {
        InitializeLibVlc();
        _libVlc = new LibVLC(
            "--no-video-title-show",
            "--quiet");
        Player = new MediaPlayer(_libVlc);
        Player.EndReached += OnEndReached;
        Player.TimeChanged += OnTimeChanged;
        Player.Playing += (_, _) => RaisePlaying(true);
        Player.Paused += (_, _) => RaisePlaying(false);
        Player.Stopped += (_, _) => RaisePlaying(false);
    }

    /// <summary>
    /// Prefer system VLC.app on Apple Silicon (NuGet Mac package is x86_64-only),
    /// then bundled natives / default search paths.
    /// </summary>
    private static void InitializeLibVlc()
    {
        var candidates = new[]
        {
            "/Applications/VLC.app/Contents/MacOS/lib",
            Path.Combine(AppContext.BaseDirectory, "libvlc", "osx-arm64", "lib"),
            Path.Combine(AppContext.BaseDirectory, "libvlc", "osx-x64", "lib"),
            AppContext.BaseDirectory,
            "/opt/homebrew/lib",
            "/usr/local/lib",
        };

        Exception? last = null;
        foreach (var libDir in candidates)
        {
            if (string.IsNullOrWhiteSpace(libDir) || !Directory.Exists(libDir))
                continue;
            var hasVlc = File.Exists(Path.Combine(libDir, "libvlc.dylib"))
                         || File.Exists(Path.Combine(libDir, "libvlc.5.dylib"));
            if (!hasVlc)
                continue;

            try
            {
                var pluginDir = libDir.EndsWith("/lib", StringComparison.Ordinal)
                    ? Path.GetFullPath(Path.Combine(libDir, "..", "plugins"))
                    : Path.Combine(libDir, "plugins");
                if (Directory.Exists(pluginDir))
                    Environment.SetEnvironmentVariable("VLC_PLUGIN_PATH", pluginDir);

                Core.Initialize(libDir);
                return;
            }
            catch (Exception ex)
            {
                last = ex;
            }
        }

        try
        {
            Core.Initialize();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Failed to load LibVLC. Install VLC from https://www.videolan.org/ or include VideoLAN.LibVLC.* for your platform.",
                last ?? ex);
        }
    }

    public bool IsPlaying => Player.IsPlaying;

    /// <summary>
    /// True when the player is opening, buffering, playing, or paused (Stop would do real work).
    /// </summary>
    public bool IsActive
    {
        get
        {
            var s = Player.State;
            return s is VLCState.Opening or VLCState.Buffering or VLCState.Playing or VLCState.Paused;
        }
    }

    public long Length => Player.Length;
    public long Time
    {
        get => Player.Time;
        set
        {
            if (Player.Length > 0)
                Player.Time = Math.Clamp(value, 0, Player.Length);
        }
    }

    public int Volume
    {
        get => Player.Volume;
        set => Player.Volume = Math.Clamp(value, 0, 100);
    }

    public bool Mute
    {
        get => Player.Mute;
        set => Player.Mute = value;
    }

    /// <summary>Playback rate (1.0 = normal). Clamped to a practical range.</summary>
    public void SetRate(float rate)
    {
        rate = Math.Clamp(rate, 0.25f, 4.0f);
        try
        {
            Player.SetRate(rate);
        }
        catch
        {
            // Some states reject rate changes; ignore.
        }
    }

    /// <param name="requireVideoHost">
    /// When true, refuse to start until <see cref="MediaPlayer.Hwnd"/> is set,
    /// so LibVLC does not open a floating top-level video window.
    /// </param>
    public bool Play(string path, bool requireVideoHost = false)
    {
        if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
            return false;

        if (requireVideoHost && Player.Hwnd == IntPtr.Zero)
            return false;

        // Swap media without an explicit Stop — faster for playlist skip (mp3/mp4).
        var next = new Media(_libVlc, path, FromType.FromPath);
        var previous = _currentMedia;
        _currentMedia = next;
        var ok = Player.Play(next);
        DisposeMediaDeferred(previous);
        return ok;
    }

    /// <summary>Starts a HTTP(S) media source, including services LibVLC can resolve.</summary>
    public bool PlayUrl(string url, bool requireVideoHost = false)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return false;

        if (requireVideoHost && Player.Hwnd == IntPtr.Zero)
            return false;

        var next = new Media(_libVlc, uri.AbsoluteUri, FromType.FromLocation);
        var previous = _currentMedia;
        _currentMedia = next;
        var ok = Player.Play(next);
        DisposeMediaDeferred(previous);
        return ok;
    }

    public bool HasVideoHost => Player.Hwnd != IntPtr.Zero;

    public void Pause() => Player.Pause();

    public void Stop() => Player.Stop();

    /// <summary>Skip no-op Stop when already idle — Stop() can stall the UI thread.</summary>
    public void StopIfActive()
    {
        if (!IsActive)
            return;
        try
        {
            Player.Stop();
        }
        catch
        {
            // Ignore stop failures during rapid track changes.
        }
    }

    private static void DisposeMediaDeferred(Media? media)
    {
        if (media is null)
            return;
        // Dispose after the player has taken the new media; avoid blocking track switch.
        Dispatcher.UIThread.Post(() =>
        {
            try { media.Dispose(); }
            catch { /* ignore */ }
        }, DispatcherPriority.Background);
    }

    public void TogglePause()
    {
        if (!Player.IsPlaying && Player.Media is null)
            return;
        Player.Pause();
    }

    public bool IsSeekable => Player.IsSeekable;

    /// <summary>
    /// Jump to a 0–1 position. Prefer absolute Time when length is known (mp3/vbr-friendly),
    /// otherwise fractional Position. Do not set both — dual seeks can cancel on some demuxers.
    /// </summary>
    public void SeekRatio(double ratio)
    {
        ratio = Math.Clamp(ratio, 0, 1);

        try
        {
            var length = Player.Length;
            if (length > 0)
            {
                // Millisecond seek is more reliable for local audio (mp3) and long files.
                Player.Time = (long)Math.Round(length * ratio);
                return;
            }

            Player.Position = (float)ratio;
        }
        catch
        {
            try
            {
                Player.Position = (float)Math.Clamp(ratio, 0, 1);
            }
            catch
            {
                // Some states reject seek; ignore.
            }
        }
    }

    /// <summary>Seek by a relative number of seconds (negative = rewind).</summary>
    public void SeekBySeconds(double seconds)
    {
        var length = Player.Length;
        if (length > 0)
        {
            var next = Math.Clamp(Player.Time + (long)(seconds * 1000), 0, length);
            try
            {
                Player.Time = next;
            }
            catch
            {
                SeekRatio((double)next / length);
            }
            return;
        }

        // Approximate via Position if length unknown (~10 min placeholder).
        var pos = Player.Position + (float)(seconds / 600.0);
        SeekRatio(pos);
    }

    public double GetProgressRatio()
    {
        if (Player.Length > 0)
            return Math.Clamp((double)Player.Time / Player.Length, 0, 1);
        return Math.Clamp(Player.Position, 0f, 1f);
    }

    private void OnEndReached(object? sender, EventArgs e)
        => Dispatcher.UIThread.Post(() => EndReached?.Invoke());

    private void OnTimeChanged(object? sender, MediaPlayerTimeChangedEventArgs e)
    {
        var length = Player.Length;
        var time = e.Time;
        Dispatcher.UIThread.Post(() => TimeChanged?.Invoke(time, length));
    }

    private void RaisePlaying(bool playing)
        => Dispatcher.UIThread.Post(() => PlayingChanged?.Invoke(playing));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Player.EndReached -= OnEndReached;
        Player.TimeChanged -= OnTimeChanged;
        Player.Stop();
        _currentMedia?.Dispose();
        Player.Dispose();
        _libVlc.Dispose();
    }
}
