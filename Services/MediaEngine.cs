using System;
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
        Core.Initialize();
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

    public bool IsPlaying => Player.IsPlaying;

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

        _currentMedia?.Dispose();
        _currentMedia = new Media(_libVlc, path, FromType.FromPath);
        return Player.Play(_currentMedia);
    }

    /// <summary>Starts a HTTP(S) media source, including services LibVLC can resolve.</summary>
    public bool PlayUrl(string url, bool requireVideoHost = false)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return false;

        if (requireVideoHost && Player.Hwnd == IntPtr.Zero)
            return false;

        _currentMedia?.Dispose();
        _currentMedia = new Media(_libVlc, uri.AbsoluteUri, FromType.FromLocation);
        return Player.Play(_currentMedia);
    }

    public bool HasVideoHost => Player.Hwnd != IntPtr.Zero;

    public void Pause() => Player.Pause();

    public void Stop() => Player.Stop();

    public void TogglePause()
    {
        if (!Player.IsPlaying && Player.Media is null)
            return;
        Player.Pause();
    }

    public bool IsSeekable => Player.IsSeekable;

    /// <summary>Jump to a 0–1 position. Uses Position first (works when Length is still 0).</summary>
    public void SeekRatio(double ratio)
    {
        ratio = Math.Clamp(ratio, 0, 1);

        // Prefer absolute time when duration is known (more precise for long files).
        if (Player.Length > 0)
        {
            Player.Time = (long)(Player.Length * ratio);
            return;
        }

        // Fallback: fractional position (LibVLC 0.0–1.0).
        try
        {
            Player.Position = (float)ratio;
        }
        catch
        {
            // Some states reject Position; ignore.
        }
    }

    /// <summary>Seek by a relative number of seconds (negative = rewind).</summary>
    public void SeekBySeconds(double seconds)
    {
        if (Player.Length <= 0)
        {
            // Approximate via Position if length unknown.
            var pos = Player.Position + (float)(seconds / 600.0); // assume ~10 min if unknown
            SeekRatio(pos);
            return;
        }

        var next = Player.Time + (long)(seconds * 1000);
        Player.Time = Math.Clamp(next, 0, Player.Length);
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
