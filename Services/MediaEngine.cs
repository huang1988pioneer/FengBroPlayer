using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using LibVLCSharp.Shared;

namespace MusicVideoMediaPlayer.Services;

/// <summary>
/// Thin LibVLC wrapper for audio/video playback with UI-thread callbacks.
/// </summary>
public sealed class MediaEngine : IDisposable
{
    private readonly LibVLC _libVlc;
    private readonly object _mediaGate = new();
    private readonly List<Media> _pendingRelease = [];
    private Media? _currentMedia;
    private bool _disposed;
    private bool _releaseDrainQueued;

    public MediaPlayer Player { get; }

    public event Action? EndReached;
    public event Action<long, long>? TimeChanged; // timeMs, lengthMs
    public event Action<bool>? PlayingChanged;

    public MediaEngine()
    {
        InitializeLibVlc();
        // Avoid --avcodec-hw=any: hardware decoder reset on file switch freezes many GPUs.
        _libVlc = new LibVLC(
            "--no-video-title-show",
            "--quiet",
            "--no-snapshot-preview",
            "--avcodec-hw=none",
            "--file-caching=300",
            "--network-caching=1000");
        Player = new MediaPlayer(_libVlc);
        Player.EndReached += OnEndReached;
        Player.TimeChanged += OnTimeChanged;
        Player.Playing += OnPlaying;
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
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return false;

        if (requireVideoHost && Player.Hwnd == IntPtr.Zero)
            return false;

        Media next;
        try
        {
            next = new Media(_libVlc, path, FromType.FromPath);
        }
        catch
        {
            return false;
        }

        return SwapAndPlay(next);
    }

    /// <summary>
    /// Starts a network media source (http/https/rtsp/…).
    /// Adds network caching options so remote streams start more reliably.
    /// </summary>
    public bool PlayUrl(string url, bool requireVideoHost = false)
    {
        if (!TryNormalizeStreamUri(url, out var uri))
            return false;

        if (requireVideoHost && Player.Hwnd == IntPtr.Zero)
            return false;

        return PlayDirectUrl(uri.AbsoluteUri, audioSlaveUrl: null, requireVideoHost);
    }

    /// <summary>
    /// Play a direct media URL (e.g. yt-dlp resolved googlevideo link).
    /// Optional <paramref name="audioSlaveUrl"/> for DASH video+audio pairs.
    /// </summary>
    public bool PlayDirectUrl(
        string streamUrl,
        string? audioSlaveUrl = null,
        bool requireVideoHost = false,
        string? httpReferrer = null)
    {
        if (string.IsNullOrWhiteSpace(streamUrl))
            return false;

        if (requireVideoHost && Player.Hwnd == IntPtr.Zero)
            return false;

        Media next;
        try
        {
            next = new Media(_libVlc, streamUrl.Trim(), FromType.FromLocation);
            next.AddOption(":network-caching=3000");
            next.AddOption(":live-caching=3000");
            next.AddOption(":http-reconnect");
            next.AddOption(":http-user-agent=Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            if (!string.IsNullOrWhiteSpace(httpReferrer))
                next.AddOption(":http-referrer=" + httpReferrer);
            else if (streamUrl.Contains("googlevideo.com", StringComparison.OrdinalIgnoreCase)
                     || streamUrl.Contains("youtube.com", StringComparison.OrdinalIgnoreCase))
                next.AddOption(":http-referrer=https://www.youtube.com/");

            if (!string.IsNullOrWhiteSpace(audioSlaveUrl))
            {
                // AddOption receives one complete VLC option, not a shell command.
                // Escaping the scheme colon changes https:// into an invalid URI for
                // LibVLC, so Bilibili's separate DASH audio stream was discarded.
                next.AddOption(":input-slave=" + audioSlaveUrl.Trim());
            }
        }
        catch
        {
            return false;
        }

        return SwapAndPlay(next);
    }

    /// <summary>
    /// Soft media swap: mute+pause then Play(new). Never call Stop() here — it freezes the UI.
    /// Old Media is kept until later; never dispose while LibVLC may still reference it.
    /// </summary>
    private bool SwapAndPlay(Media next)
    {
        // Soft hand-off: pause current decode before attaching new media.
        try
        {
            var s = Player.State;
            if (s is VLCState.Playing or VLCState.Buffering or VLCState.Opening)
                Player.SetPause(true);
        }
        catch { /* ignore */ }

        Media? previous;
        lock (_mediaGate)
        {
            previous = _currentMedia;
            _currentMedia = next;
        }

        bool ok;
        try
        {
            ok = Player.Play(next);
        }
        catch
        {
            ok = false;
        }

        if (previous is not null)
            QueueMediaRelease(previous);

        return ok;
    }

    private void QueueMediaRelease(Media media)
    {
        lock (_mediaGate)
        {
            _pendingRelease.Add(media);
            // Cap queue size without disposing here (Dispose on UI during skip freezes).
            while (_pendingRelease.Count > 12)
                _pendingRelease.RemoveAt(0);

            if (_releaseDrainQueued)
                return;
            _releaseDrainQueued = true;
        }

        // Long delay: disposing Media mid-decode is a primary freeze source on Windows.
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(2500).ConfigureAwait(false);
            }
            catch { /* ignore */ }

            Dispatcher.UIThread.Post(() =>
            {
                try { DrainReleasedMedia(); }
                finally
                {
                    lock (_mediaGate) _releaseDrainQueued = false;
                }
            }, DispatcherPriority.Background);
        });
    }

    private void DrainReleasedMedia()
    {
        List<Media> batch;
        lock (_mediaGate)
        {
            if (_pendingRelease.Count == 0)
                return;
            batch = [.. _pendingRelease];
            _pendingRelease.Clear();
        }

        Media? stillCurrent;
        Media? stillPlayer;
        try { stillPlayer = Player.Media; }
        catch { stillPlayer = null; }
        lock (_mediaGate) stillCurrent = _currentMedia;

        foreach (var media in batch)
        {
            if (ReferenceEquals(media, stillCurrent) || ReferenceEquals(media, stillPlayer))
            {
                lock (_mediaGate) _pendingRelease.Add(media);
                continue;
            }

            try { media.Dispose(); }
            catch { /* ignore */ }
        }
    }

    /// <summary>Accepts http(s), rtsp, rtmp, mms, and URLs missing a scheme (defaults to https).</summary>
    public static bool TryNormalizeStreamUri(string? url, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(url))
            return false;

        var s = url.Trim().Trim('<', '>', '"', '\'');
        if (s.StartsWith("//", StringComparison.Ordinal))
            s = "https:" + s;
        else if (!s.Contains("://", StringComparison.Ordinal))
            s = "https://" + s;

        if (!Uri.TryCreate(s, UriKind.Absolute, out var parsed) || !parsed.IsAbsoluteUri)
            return false;

        var scheme = parsed.Scheme;
        if (scheme is not ("http" or "https" or "rtsp" or "rtsps" or "rtmp" or "rtmps" or "mms" or "mmsh" or "rtp"))
            return false;

        uri = parsed;
        return true;
    }

    public bool HasVideoHost => Player.Hwnd != IntPtr.Zero;

    public void Pause() => Player.Pause();

    public void Stop() => Player.Stop();

    /// <summary>Skip no-op Stop when already idle — Stop() can stall the UI thread.</summary>
    public void StopIfActive()
    {
        var s = Player.State;
        if (s is VLCState.NothingSpecial or VLCState.Stopped or VLCState.Error)
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

    /// <summary>
    /// Mute + pause the inactive engine. Never Stop() — it freezes the UI thread on Windows.
    /// </summary>
    public void YieldForOtherEngine()
    {
        try
        {
            if (Player.Volume != 0)
                Player.Volume = 0;
        }
        catch { /* ignore */ }

        try
        {
            var s = Player.State;
            if (s is VLCState.Playing or VLCState.Buffering or VLCState.Opening)
                Player.SetPause(true);
        }
        catch
        {
            // Ignore.
        }
    }

    /// <summary>
    /// Soft teardown only (no Stop, no Hwnd clear). Video HWND is kept for the session.
    /// Kept for call sites that previously destroyed the host.
    /// </summary>
    public void PrepareForHostTeardown() => YieldForOtherEngine();

    public void TogglePause()
    {
        if (!Player.IsPlaying && Player.Media is null)
            return;
        Player.Pause();
    }

    public bool IsSeekable => Player.IsSeekable;

    /// <summary>
    /// Loads an external subtitle file (SRT, ASS, VTT…) into the running player.
    /// Must be called after Play() so the media is already attached.
    /// Returns true when the slave was accepted by LibVLC.
    /// </summary>
    public bool AddSubtitleFile(string path)
    {
        if (!File.Exists(path))
            return false;
        try
        {
            // Build a file:// URI that LibVLC understands on all platforms.
            var uri = new Uri(path).AbsoluteUri;
            return Player.AddSlave(MediaSlaveType.Subtitle, uri, select: true);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Disables all subtitle tracks (sets the active SPU track to -1).
    /// The subtitle data stays loaded but becomes invisible.
    /// </summary>
    public void ClearSubtitles()
    {
        try
        {
            Player.SetSpu(-1);
        }
        catch { /* ignore */ }
    }

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

    private void OnPlaying(object? sender, EventArgs e)
    {
        RaisePlaying(true);
        // A media swap after a DASH stream can leave LibVLC on the disabled
        // audio track. Select the first real track once parsing has completed.
        Dispatcher.UIThread.Post(async () =>
        {
            await Task.Delay(180);
            if (!_disposed)
                SelectFirstAudioTrack();
        }, DispatcherPriority.Background);
    }

    /// <summary>Draws text inside the native LibVLC video surface (OSD).</summary>
    public void SetVideoInfoOverlay(string? text)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                Player.SetMarqueeInt(VideoMarqueeOption.Enable, 0);
                return;
            }

            Player.SetMarqueeString(VideoMarqueeOption.Text, text);
            Player.SetMarqueeInt(VideoMarqueeOption.Color, 0xFFFFFF);
            Player.SetMarqueeInt(VideoMarqueeOption.Opacity, 235);
            // libvlc_position_t: TopLeft = 5.
            Player.SetMarqueeInt(VideoMarqueeOption.Position, 5);
            Player.SetMarqueeInt(VideoMarqueeOption.Size, 18);
            Player.SetMarqueeInt(VideoMarqueeOption.Timeout, 0);
            Player.SetMarqueeInt(VideoMarqueeOption.Enable, 1);
        }
        catch
        {
            // OSD is unavailable before the native video output is ready.
        }
    }

    private void SelectFirstAudioTrack()
    {
        try
        {
            var track = Player.AudioTrackDescription?.FirstOrDefault(t => t.Id >= 0);
            if (track is not null)
                Player.SetAudioTrack(track.Value.Id);
        }
        catch
        {
            // Audio-only media and streams without a parsed track are valid.
        }
    }

    private long _lastTimeUiPostMs;

    private void OnTimeChanged(object? sender, MediaPlayerTimeChangedEventArgs e)
    {
        // Throttle: LibVLC can fire hundreds of times/sec and flood the UI dispatcher
        // during rapid file switches, causing multi-second freezes.
        var now = Environment.TickCount64;
        if (now - _lastTimeUiPostMs < 120)
            return;
        _lastTimeUiPostMs = now;

        var length = Player.Length;
        var time = e.Time;
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed) return;
            TimeChanged?.Invoke(time, length);
        }, DispatcherPriority.Background);
    }

    private void RaisePlaying(bool playing)
        => Dispatcher.UIThread.Post(() =>
        {
            if (_disposed) return;
            PlayingChanged?.Invoke(playing);
        }, DispatcherPriority.Normal);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Player.EndReached -= OnEndReached;
        Player.Playing -= OnPlaying;
        Player.TimeChanged -= OnTimeChanged;
        try { Player.Stop(); } catch { /* ignore */ }
        DrainReleasedMedia();
        lock (_mediaGate)
        {
            try { _currentMedia?.Dispose(); } catch { /* ignore */ }
            _currentMedia = null;
            foreach (var m in _pendingRelease)
            {
                try { m.Dispose(); } catch { /* ignore */ }
            }
            _pendingRelease.Clear();
        }
        Player.Dispose();
        _libVlc.Dispose();
    }
}
