using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibVLCSharp.Shared;
using MusicVideoMediaPlayer.Models;
using MusicVideoMediaPlayer.Services;

namespace MusicVideoMediaPlayer.ViewModels;

public partial class MainViewModel : ViewModelBase, IDisposable
{
    private readonly MediaEngine _audio = new();
    private readonly MediaEngine _video = new();
    private bool _isSeeking;
    private bool _disposed;
    private bool _playlistVisibleBeforeFs = true;
    private double _volumeBeforeMute = 1.0;
    private bool _suppressChromeSideEffects;

    /// <summary>Set by the view to open native file pickers.</summary>
    public Func<string, Task<IReadOnlyList<string>>>? PickFilesAsync { get; set; }

    /// <summary>
    /// Called by the view before video Play so LibVLC gets an embedded HWND
    /// (prevents a floating top-level video window).
    /// </summary>
    public Action? PrepareVideoHost { get; set; }

    public Action? RequestFullscreen { get; set; }
    public Action? ExitFullscreen { get; set; }
    public Action? RequestClose { get; set; }
    public Action<bool>? SetTopmost { get; set; }
    public Func<Task<string?>>? PromptNetworkUrlAsync { get; set; }

    public ObservableCollection<MediaItem> Playlist { get; } = [];
    public ObservableCollection<double> WaveformBars { get; } = [];

    /// <summary>Bound to LibVLC VideoView for real video rendering (Stage A uses video engine).</summary>
    public MediaPlayer VideoMediaPlayer => _video.Player;

    [ObservableProperty] public partial ChromeMode CurrentChrome { get; set; } = ChromeMode.Normal;
    [ObservableProperty] public partial bool IsPlaylistVisible { get; set; } = true;
    [ObservableProperty] public partial bool IsMenuBarVisible { get; set; } = true;
    [ObservableProperty] public partial bool IsControlBarVisible { get; set; } = true;
    [ObservableProperty] public partial bool IsStatusBarVisible { get; set; } = true;
    [ObservableProperty] public partial bool IsAlwaysOnTop { get; set; }
    [ObservableProperty] public partial double PlaybackRate { get; set; } = 1.0;

    [ObservableProperty] public partial MediaKind ActiveMediaKind { get; set; } = MediaKind.None;
    [ObservableProperty] public partial MediaItem? CurrentMedia { get; set; }
    [ObservableProperty] public partial bool IsPlaying { get; set; }
    [ObservableProperty] public partial double Progress { get; set; }
    [ObservableProperty] public partial string PositionText { get; set; } = "00:00";
    [ObservableProperty] public partial string DurationText { get; set; } = "00:00";
    [ObservableProperty] public partial string WindowTitle { get; set; } = "多媒體播放器";
    [ObservableProperty] public partial bool IsMuted { get; set; }
    [ObservableProperty] public partial string StatusMessage { get; set; } = "就緒 — 可開啟本機音樂或影片檔案";
    [ObservableProperty] public partial string StatusDetail { get; set; } = "";
    [ObservableProperty] public partial bool AutoPlay { get; set; } = true;
    [ObservableProperty] public partial double Volume { get; set; } = 1.0;
    [ObservableProperty] public partial string NetworkUrl { get; set; } = string.Empty;

    [ObservableProperty] public partial bool IsVideoStage { get; set; }
    [ObservableProperty] public partial bool IsAudioStage { get; set; }
    [ObservableProperty] public partial bool HasPlayableMedia { get; set; }

    public MainViewModel()
    {
        SeedWaveform();
        ApplyStageFlags(MediaKind.None);

        _audio.Volume = (int)(Volume * 100);
        _video.Volume = (int)(Volume * 100);

        _audio.TimeChanged += (t, l) => OnEngineTimeChanged(MediaKind.Audio, t, l);
        _video.TimeChanged += (t, l) => OnEngineTimeChanged(MediaKind.Video, t, l);
        _audio.EndReached += () => OnEngineEndReached(MediaKind.Audio);
        _video.EndReached += () => OnEngineEndReached(MediaKind.Video);
        _audio.PlayingChanged += playing => OnEnginePlayingChanged(MediaKind.Audio, playing);
        _video.PlayingChanged += playing => OnEnginePlayingChanged(MediaKind.Video, playing);
    }

    partial void OnActiveMediaKindChanged(MediaKind value) => ApplyStageFlags(value);

    private void ApplyStageFlags(MediaKind value)
    {
        IsVideoStage = value == MediaKind.Video;
        IsAudioStage = value == MediaKind.Audio || value == MediaKind.None;
    }

    partial void OnVolumeChanged(double value)
    {
        var vol = (int)(Math.Clamp(value, 0, 1) * 100);
        _audio.Volume = vol;
        _video.Volume = vol;
        if (vol > 0 && IsMuted)
        {
            IsMuted = false;
            _audio.Mute = false;
            _video.Mute = false;
        }
    }

    partial void OnProgressChanged(double value)
    {
        // While scrubbing, only refresh the time label. Actual LibVLC seek runs in EndSeek
        // so video demuxers (e.g. MP4) are not thrashed by intermediate Position writes.
        if (!_isSeeking || CurrentMedia?.IsPlayable != true)
            return;

        UpdatePositionLabel(value);
    }

    partial void OnPlaybackRateChanged(double value)
    {
        var rate = (float)Math.Clamp(value, 0.25, 4.0);
        ActiveEngine?.SetRate(rate);
    }

    partial void OnIsAlwaysOnTopChanged(bool value) => SetTopmost?.Invoke(value);

    partial void OnCurrentChromeChanged(ChromeMode value)
    {
        if (_suppressChromeSideEffects) return;

        if (value == ChromeMode.Fullscreen)
        {
            _playlistVisibleBeforeFs = IsPlaylistVisible;
            IsPlaylistVisible = false;
            IsMenuBarVisible = false;
            IsStatusBarVisible = false;
            IsControlBarVisible = true;
            RequestFullscreen?.Invoke();
        }
        else if (value == ChromeMode.Normal)
        {
            IsPlaylistVisible = _playlistVisibleBeforeFs;
            IsMenuBarVisible = true;
            IsStatusBarVisible = true;
            IsControlBarVisible = true;
            ExitFullscreen?.Invoke();
        }
    }

    private MediaEngine? ActiveEngine => ActiveMediaKind switch
    {
        MediaKind.Audio => _audio,
        MediaKind.Video => _video,
        _ => null
    };

    private void SeedWaveform()
    {
        var rnd = new Random(42);
        for (var i = 0; i < 48; i++)
        {
            var envelope = Math.Sin(i / 48.0 * Math.PI);
            var noise = 0.25 + rnd.NextDouble() * 0.75;
            WaveformBars.Add(Math.Clamp(envelope * noise, 0.12, 1.0));
        }
    }

    private void ReindexPlaylist()
    {
        for (var i = 0; i < Playlist.Count; i++)
            Playlist[i].Index = i + 1;
    }

    private void MarkCurrent(MediaItem? item)
    {
        foreach (var m in Playlist)
            m.IsCurrent = false;
        if (item is not null)
            item.IsCurrent = true;
        CurrentMedia = item;
        HasPlayableMedia = item?.IsPlayable == true;
        if (item is not null)
        {
            WindowTitle = $"{item.Title} — 多媒體播放器";
            DurationText = item.Duration;
            PositionText = "00:00";
            Progress = 0;
            StatusDetail = string.Join(" · ",
                new[] { item.Format, item.Bitrate, item.Kind == MediaKind.Video ? "影片" : "音樂" }
                    .Where(s => !string.IsNullOrWhiteSpace(s)));
        }
        else
        {
            WindowTitle = "多媒體播放器";
            StatusDetail = "";
        }
    }

    [RelayCommand]
    private void SelectMedia(MediaItem? item)
    {
        if (item is null) return;

        MarkCurrent(item);
        ActiveMediaKind = item.Kind == MediaKind.None ? MediaKind.None : item.Kind;

        if (item.Kind == MediaKind.Audio)
        {
            _video.Stop();
            if (item.IsLocalFile && item.FilePath is not null)
            {
                _audio.Play(item.FilePath);
                _audio.SetRate((float)PlaybackRate);
                IsPlaying = true;
                StatusMessage = $"正在播放：{item.Title}";
            }
            else
            {
                _audio.Stop();
                IsPlaying = true; // demo visual
                StatusMessage = $"示範曲目（無檔案）：{item.Title} — 請開啟本機音樂";
            }
        }
        else if (item.Kind == MediaKind.Video)
        {
            _audio.Stop();
            if (item.IsLocalFile && item.FilePath is not null)
            {
                PlayLocalVideo(item.FilePath, item.Title);
            }
            else if (item.IsNetworkSource && item.SourceUrl is not null)
            {
                PlayNetworkVideo(item.SourceUrl, item.Title);
            }
            else
            {
                _video.Stop();
                IsPlaying = false;
                StatusMessage = $"示範影片（無檔案）：{item.Title} — 請開啟本機影片";
            }
        }
    }

    private void PlayLocalVideo(string path, string title)
    {
        PrepareVideoHost?.Invoke();
        if (_video.Play(path, requireVideoHost: true))
        {
            _video.SetRate((float)PlaybackRate);
            IsPlaying = true;
            StatusMessage = $"正在播放影片：{title}";
            return;
        }

        Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
        {
            for (var i = 0; i < 10; i++)
            {
                PrepareVideoHost?.Invoke();
                if (_video.Play(path, requireVideoHost: true))
                {
                    _video.SetRate((float)PlaybackRate);
                    IsPlaying = true;
                    StatusMessage = $"正在播放影片：{title}";
                    return;
                }
                await Task.Delay(50);
            }
            StatusMessage = "無法嵌入影片畫面（主機控制項尚未就緒）。請再按一次播放。";
            IsPlaying = false;
        }, Avalonia.Threading.DispatcherPriority.Background);
    }

    private void PlayNetworkVideo(string url, string title)
    {
        PrepareVideoHost?.Invoke();
        if (_video.PlayUrl(url, requireVideoHost: true))
        {
            _video.SetRate((float)PlaybackRate);
            IsPlaying = true;
            StatusMessage = $"正在播放網路影片：{title}";
            return;
        }

        StatusMessage = "無法開始此網路影片。請檢查網址或 LibVLC 支援。";
        IsPlaying = false;
    }

    [RelayCommand]
    private void TogglePlay()
    {
        var item = CurrentMedia;
        if (item is null)
        {
            var first = FindPlayable(fromIndex: -1, direction: 1);
            if (first is not null) SelectMedia(first);
            return;
        }

        if (!item.IsPlayable)
        {
            IsPlaying = !IsPlaying;
            StatusMessage = IsPlaying
                ? $"示範媒體：{item.Title}（無實際播放）"
                : "已暫停示範媒體";
            return;
        }

        var engine = ActiveEngine;
        if (engine is null) return;

        if (engine.IsPlaying)
        {
            engine.Pause();
            IsPlaying = false;
            return;
        }

        // Resume after pause, or re-open after Stop.
        if (engine.Player.Media is not null)
        {
            if (item.Kind == MediaKind.Video)
                PrepareVideoHost?.Invoke();
            engine.Player.Play();
            engine.SetRate((float)PlaybackRate);
            IsPlaying = true;
            return;
        }

        if (item.FilePath is not null)
        {
            if (item.Kind == MediaKind.Video)
                PlayLocalVideo(item.FilePath, item.Title);
            else
            {
                _audio.Play(item.FilePath);
                _audio.SetRate((float)PlaybackRate);
                IsPlaying = true;
                StatusMessage = $"正在播放：{item.Title}";
            }
        }
        else if (item.SourceUrl is not null)
        {
            PlayNetworkVideo(item.SourceUrl, item.Title);
        }
    }

    [RelayCommand]
    private void StopMedia()
    {
        _audio.Stop();
        _video.Stop();
        IsPlaying = false;
        Progress = 0;
        PositionText = "00:00";
        StatusMessage = CurrentMedia is null ? "已停止" : $"已停止：{CurrentMedia.Title}";
    }

    [RelayCommand]
    private void PlayPrevious()
    {
        if (Playlist.Count == 0) return;
        var from = CurrentMedia is null ? 0 : Playlist.ToList().FindIndex(m => ReferenceEquals(m, CurrentMedia));
        if (from < 0) from = 0;
        var prev = FindPlayable(from, direction: -1);
        if (prev is null)
        {
            StopMedia();
            StatusMessage = "清單中沒有可播放的媒體";
            return;
        }
        SelectMedia(prev);
    }

    [RelayCommand]
    private void PlayNext()
    {
        if (Playlist.Count == 0) return;
        var from = CurrentMedia is null ? -1 : Playlist.ToList().FindIndex(m => ReferenceEquals(m, CurrentMedia));
        var next = FindPlayable(from, direction: 1);
        if (next is null)
        {
            StopMedia();
            StatusMessage = "清單中沒有可播放的媒體";
            return;
        }
        SelectMedia(next);
    }

    /// <summary>
    /// Scan playlist for next/prev playable item. direction: +1 or -1.
    /// fromIndex = starting index (exclusive for step). Wrap once.
    /// </summary>
    private MediaItem? FindPlayable(int fromIndex, int direction)
    {
        if (Playlist.Count == 0) return null;
        var n = Playlist.Count;
        for (var step = 1; step <= n; step++)
        {
            var idx = ((fromIndex + direction * step) % n + n) % n;
            var item = Playlist[idx];
            if (item.IsPlayable)
                return item;
        }
        return null;
    }

    [RelayCommand]
    private async Task OpenMediaAsync()
    {
        if (PickFilesAsync is null)
        {
            StatusMessage = "檔案對話框尚未就緒";
            return;
        }

        var paths = await PickFilesAsync("media");
        if (paths.Count == 0) return;
        ImportPaths(paths);
    }

    [RelayCommand]
    private async Task OpenMusicAsync()
    {
        if (PickFilesAsync is null)
        {
            StatusMessage = "檔案對話框尚未就緒";
            return;
        }

        var paths = await PickFilesAsync("audio");
        if (paths.Count == 0) return;
        ImportPaths(paths);
    }

    [RelayCommand]
    private async Task OpenVideoAsync()
    {
        if (PickFilesAsync is null)
        {
            StatusMessage = "檔案對話框尚未就緒";
            return;
        }

        var paths = await PickFilesAsync("video");
        if (paths.Count == 0) return;
        ImportPaths(paths);
    }

    public void ImportDroppedPaths(IEnumerable<string> paths)
    {
        var list = paths.Where(File.Exists).ToList();
        if (list.Count == 0) return;
        ImportPaths(list);
    }

    private void ImportPaths(IReadOnlyList<string> paths)
    {
        MediaItem? firstNewPlayable = null;
        var added = 0;

        foreach (var path in paths)
        {
            if (Playlist.Any(m => string.Equals(m.FilePath, path, StringComparison.OrdinalIgnoreCase)))
                continue;

            MediaItem item;
            if (MediaMetadata.IsVideo(path))
            {
                var info = MediaMetadata.ReadVideo(path);
                item = new MediaItem
                {
                    Index = Playlist.Count + 1,
                    Title = info.Title,
                    Subtitle = $"本機影片 · {info.Format}",
                    Duration = info.Duration,
                    Kind = MediaKind.Video,
                    FilePath = path,
                    CoverHue = MediaMetadata.HueFromPath(path),
                    Format = info.Format
                };
            }
            else if (MediaMetadata.IsAudio(path))
            {
                var info = MediaMetadata.ReadAudio(path);
                item = new MediaItem
                {
                    Index = Playlist.Count + 1,
                    Title = info.Title,
                    Subtitle = info.Artist,
                    Duration = info.Duration,
                    Kind = MediaKind.Audio,
                    FilePath = path,
                    CoverHue = MediaMetadata.HueFromPath(path),
                    Format = info.Format,
                    Bitrate = info.Bitrate
                };
            }
            else
            {
                // Unknown extension: try as audio path name heuristic
                var name = Path.GetFileNameWithoutExtension(path);
                var asVideo = path.Contains("video", StringComparison.OrdinalIgnoreCase);
                item = new MediaItem
                {
                    Index = Playlist.Count + 1,
                    Title = name,
                    Subtitle = asVideo ? "本機影片" : "本機音樂",
                    Duration = "—:—",
                    Kind = asVideo ? MediaKind.Video : MediaKind.Audio,
                    FilePath = path,
                    CoverHue = MediaMetadata.HueFromPath(path),
                    Format = Path.GetExtension(path).TrimStart('.').ToUpperInvariant()
                };
            }

            Playlist.Add(item);
            added++;
            firstNewPlayable ??= item.IsPlayable ? item : null;
        }

        ReindexPlaylist();
        StatusMessage = added > 0 ? $"已加入 {added} 個媒體檔案" : "未加入新檔案（可能重複）";

        if (firstNewPlayable is not null)
            SelectMedia(firstNewPlayable);
        else if (added == 0 && paths.Count > 0)
            StatusMessage = "未辨識到可支援的媒體格式";
    }

    [RelayCommand]
    private async Task OpenNetworkUrlAsync()
    {
        string? url = null;
        if (PromptNetworkUrlAsync is not null)
            url = await PromptNetworkUrlAsync();
        else if (!string.IsNullOrWhiteSpace(NetworkUrl))
            url = NetworkUrl.Trim();

        if (string.IsNullOrWhiteSpace(url))
        {
            StatusMessage = "已取消開啟網路串流";
            return;
        }

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            StatusMessage = "請輸入有效的 http:// 或 https:// 媒體網址";
            return;
        }

        var title = uri.Host.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) ||
                    uri.Host.Contains("youtu.be", StringComparison.OrdinalIgnoreCase)
            ? "YouTube 影片"
            : uri.Host;

        var existing = Playlist.FirstOrDefault(m =>
            string.Equals(m.SourceUrl, uri.AbsoluteUri, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            SelectMedia(existing);
            return;
        }

        var item = new MediaItem
        {
            Index = Playlist.Count + 1,
            Title = title,
            Subtitle = uri.AbsoluteUri,
            Duration = "--:--",
            Kind = MediaKind.Video,
            SourceUrl = uri.AbsoluteUri,
            CoverHue = "195",
            Format = "URL"
        };
        Playlist.Insert(0, item);
        ReindexPlaylist();
        SelectMedia(item);
    }

    [RelayCommand]
    private void ClearPlaylist()
    {
        _audio.Stop();
        _video.Stop();
        Playlist.Clear();
        CurrentMedia = null;
        ActiveMediaKind = MediaKind.None;
        IsPlaying = false;
        Progress = 0;
        PositionText = "00:00";
        DurationText = "00:00";
        WindowTitle = "多媒體播放器";
        HasPlayableMedia = false;
        StatusMessage = "已清除播放清單";
        StatusDetail = "";
    }

    [RelayCommand]
    private void TogglePlaylist() => IsPlaylistVisible = !IsPlaylistVisible;

    [RelayCommand]
    private void ToggleFullscreen()
    {
        CurrentChrome = CurrentChrome == ChromeMode.Fullscreen
            ? ChromeMode.Normal
            : ChromeMode.Fullscreen;
    }

    /// <summary>View reports system Esc or external exit without re-entering FS side effects loop.</summary>
    public void NotifyExitedFullscreen()
    {
        if (CurrentChrome != ChromeMode.Fullscreen) return;
        _suppressChromeSideEffects = true;
        CurrentChrome = ChromeMode.Normal;
        IsPlaylistVisible = _playlistVisibleBeforeFs;
        IsMenuBarVisible = true;
        IsStatusBarVisible = true;
        IsControlBarVisible = true;
        _suppressChromeSideEffects = false;
    }

    [RelayCommand]
    private void ToggleMute()
    {
        if (IsMuted)
        {
            IsMuted = false;
            _audio.Mute = false;
            _video.Mute = false;
            Volume = _volumeBeforeMute > 0 ? _volumeBeforeMute : 1.0;
            StatusMessage = "已取消靜音";
        }
        else
        {
            _volumeBeforeMute = Volume > 0 ? Volume : 1.0;
            IsMuted = true;
            _audio.Mute = true;
            _video.Mute = true;
            StatusMessage = "已靜音";
        }
    }

    [RelayCommand]
    private void SeekRelative(object? parameter)
    {
        if (CurrentMedia?.IsPlayable != true || ActiveEngine is null)
            return;

        var seconds = 5.0;
        if (parameter is int i) seconds = i;
        else if (parameter is string s && double.TryParse(s, out var d)) seconds = d;
        else if (parameter is double dd) seconds = dd;

        ActiveEngine.SeekBySeconds(seconds);
        Progress = ActiveEngine.GetProgressRatio();
        UpdatePositionLabel(Progress);
    }

    [RelayCommand]
    private void Exit() => RequestClose?.Invoke();

    public void BeginSeek() => _isSeeking = true;

    public void EndSeek()
    {
        if (_isSeeking && CurrentMedia?.IsPlayable == true && ActiveEngine is not null)
        {
            ActiveEngine.SeekRatio(Progress);
            UpdatePositionLabel(Progress);
        }
        _isSeeking = false;
    }

    private void UpdatePositionLabel(double ratio)
    {
        ratio = Math.Clamp(ratio, 0, 1);
        var engine = ActiveEngine;
        if (engine is null) return;
        var lengthMs = engine.Length;
        if (lengthMs > 0)
        {
            var timeMs = (long)(lengthMs * ratio);
            PositionText = MediaMetadata.FormatDuration(TimeSpan.FromMilliseconds(timeMs));
            DurationText = MediaMetadata.FormatDuration(TimeSpan.FromMilliseconds(lengthMs));
        }
    }

    private void OnEngineTimeChanged(MediaKind kind, long timeMs, long lengthMs)
    {
        if (kind != ActiveMediaKind) return;
        if (_isSeeking) return;

        var engine = ActiveEngine;
        if (engine is null) return;

        Progress = engine.GetProgressRatio();
        if (lengthMs > 0)
            DurationText = MediaMetadata.FormatDuration(TimeSpan.FromMilliseconds(lengthMs));
        PositionText = MediaMetadata.FormatDuration(TimeSpan.FromMilliseconds(timeMs));
    }

    private void OnEngineEndReached(MediaKind kind)
    {
        if (kind != ActiveMediaKind) return;
        IsPlaying = false;
        if (!AutoPlay)
            return;
        PlayNext();
    }

    private void OnEnginePlayingChanged(MediaKind kind, bool playing)
    {
        if (kind != ActiveMediaKind) return;
        IsPlaying = playing;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _audio.Dispose();
        _video.Dispose();
    }
}
