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
    private readonly RecentPlayStore _recent = new();
    private readonly RecentStreamStore _streams = new();
    private int _selectGeneration;
    private bool _isSelecting;
    private bool _isSeeking;
    /// <summary>Ignore engine TimeChanged until this tick so scrub UI does not snap back (mp3/mp4).</summary>
    private long _suppressTimeChangedUntilTick;
    private double _seekTargetRatio;
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
    public ObservableCollection<RecentPlayEntry> RecentItems => _recent.Items;
    public ObservableCollection<RecentPlayEntry> RecentStreamItems => _streams.Items;
    public ObservableCollection<double> WaveformBars { get; } = [];

    /// <summary>Bound to LibVLC VideoView for real video rendering (Stage A uses video engine).</summary>
    public MediaPlayer VideoMediaPlayer => _video.Player;

    [ObservableProperty] public partial ChromeMode CurrentChrome { get; set; } = ChromeMode.Normal;
    [ObservableProperty] public partial bool IsPlaylistVisible { get; set; } = true;
    [ObservableProperty] public partial SideDockPane ActiveDockPane { get; set; } = SideDockPane.Playlist;
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
    [ObservableProperty] public partial bool HasRecentItems { get; set; }
    [ObservableProperty] public partial bool HasRecentStreamItems { get; set; }

    public bool IsPlaylistDock => ActiveDockPane == SideDockPane.Playlist;
    public bool IsRecentDock => ActiveDockPane == SideDockPane.Recent;
    public bool IsStreamDock => ActiveDockPane == SideDockPane.Streams;

    public MainViewModel()
    {
        SeedWaveform();
        ApplyStageFlags(MediaKind.None);
        _recent.Load();
        _streams.Load();
        HasRecentItems = RecentItems.Count > 0;
        HasRecentStreamItems = RecentStreamItems.Count > 0;
        RecentItems.CollectionChanged += (_, _) => HasRecentItems = RecentItems.Count > 0;
        RecentStreamItems.CollectionChanged += (_, _) => HasRecentStreamItems = RecentStreamItems.Count > 0;

        _audio.Volume = (int)(Volume * 100);
        _video.Volume = (int)(Volume * 100);

        _audio.TimeChanged += (t, l) => OnEngineTimeChanged(MediaKind.Audio, t, l);
        _video.TimeChanged += (t, l) => OnEngineTimeChanged(MediaKind.Video, t, l);
        _audio.EndReached += () => OnEngineEndReached(MediaKind.Audio);
        _video.EndReached += () => OnEngineEndReached(MediaKind.Video);
        _audio.PlayingChanged += playing => OnEnginePlayingChanged(MediaKind.Audio, playing);
        _video.PlayingChanged += playing => OnEnginePlayingChanged(MediaKind.Video, playing);
    }

    partial void OnActiveDockPaneChanged(SideDockPane value)
    {
        OnPropertyChanged(nameof(IsPlaylistDock));
        OnPropertyChanged(nameof(IsRecentDock));
        OnPropertyChanged(nameof(IsStreamDock));
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
        // Only touch previous + new row — full-list IsCurrent resets freeze large playlists.
        var previous = CurrentMedia;
        if (previous is not null && !ReferenceEquals(previous, item) && previous.IsCurrent)
            previous.IsCurrent = false;

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
        // Re-entrant SelectMedia (rapid clicks / layout callbacks) can freeze LibVLC.
        if (_isSelecting)
            return;
        _isSelecting = true;
        var generation = ++_selectGeneration;
        try
        {
            SelectMediaCore(item, generation);
        }
        finally
        {
            _isSelecting = false;
        }
    }

    private void SelectMediaCore(MediaItem item, int generation)
    {
        var previousKind = ActiveMediaKind;
        var nextKind = item.Kind == MediaKind.None ? MediaKind.None : item.Kind;

        // CRITICAL: stop video and detach HWND *before* stage row collapses (Height=0 destroys
        // the native host). Clearing Hwnd while still Playing is a common hard freeze on Windows.
        if (previousKind == MediaKind.Video && nextKind != MediaKind.Video)
            _video.PrepareForHostTeardown();

        MarkCurrent(item);
        ActiveMediaKind = nextKind;
        if (generation != _selectGeneration)
            return;

        if (nextKind == MediaKind.Audio)
        {
            _video.YieldForOtherEngine();
            // Restore audio engine volume (may have been muted if we previously yielded it).
            _audio.Volume = (int)(Math.Clamp(Volume, 0, 1) * 100);

            if (item.IsLocalFile && item.FilePath is not null)
            {
                _audio.Play(item.FilePath);
                _audio.SetRate((float)PlaybackRate);
                IsPlaying = true;
                StatusMessage = $"正在播放：{item.Title}";
                RecordRecentDeferred(item);
            }
            else if (item.IsNetworkSource && item.SourceUrl is not null)
            {
                PlayNetworkAudio(item.SourceUrl, item.Title);
                if (IsPlaying)
                    RecordRecentDeferred(item);
            }
            else
            {
                _audio.StopIfActive();
                IsPlaying = true; // demo visual
                StatusMessage = $"示範曲目（無檔案）：{item.Title} — 請開啟本機音樂";
            }
        }
        else if (nextKind == MediaKind.Video)
        {
            _audio.YieldForOtherEngine();
            _video.Volume = (int)(Math.Clamp(Volume, 0, 1) * 100);

            if (item.IsLocalFile && item.FilePath is not null)
            {
                PlayLocalVideo(item.FilePath, item.Title, generation);
                RecordRecentDeferred(item);
            }
            else if (item.IsNetworkSource && item.SourceUrl is not null)
            {
                PlayNetworkVideo(item.SourceUrl, item.Title, generation);
                RecordRecentDeferred(item);
            }
            else
            {
                _video.StopIfActive();
                IsPlaying = false;
                StatusMessage = $"示範影片（無檔案）：{item.Title} — 請開啟本機影片";
            }
        }
    }

    /// <summary>History write runs after the frame so Play is not blocked by JSON/disk I/O.</summary>
    private void RecordRecentDeferred(MediaItem item)
    {
        if (!item.IsPlayable) return;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _recent.Record(item);
            if (item.IsNetworkSource)
                _streams.Record(item);
        }, Avalonia.Threading.DispatcherPriority.Background);
    }

    [RelayCommand]
    private void ShowPlaylistDock()
    {
        ActiveDockPane = SideDockPane.Playlist;
        IsPlaylistVisible = true;
    }

    [RelayCommand]
    private void ShowRecentDock()
    {
        ActiveDockPane = SideDockPane.Recent;
        IsPlaylistVisible = true;
        StatusMessage = RecentItems.Count > 0
            ? $"最近播放：{RecentItems.Count} 筆"
            : "尚無最近播放紀錄";
    }

    [RelayCommand]
    private void ShowStreamDock()
    {
        ActiveDockPane = SideDockPane.Streams;
        IsPlaylistVisible = true;
        StatusMessage = RecentStreamItems.Count > 0
            ? $"最近網路串流：{RecentStreamItems.Count} 筆"
            : "尚無網路串流播放紀錄";
    }

    [RelayCommand]
    private void PlayRecent(RecentPlayEntry? entry)
    {
        if (entry is null) return;

        if (entry.IsLocalFile && entry.FilePath is not null && !File.Exists(entry.FilePath))
        {
            StatusMessage = $"檔案已不存在，已自最近播放移除：{entry.Title}";
            _recent.Remove(entry);
            return;
        }

        // Reuse existing playlist row when possible to keep queue continuity.
        MediaItem? existing = null;
        if (entry.FilePath is not null)
        {
            existing = Playlist.FirstOrDefault(m =>
                string.Equals(m.FilePath, entry.FilePath, StringComparison.OrdinalIgnoreCase));
        }
        else if (entry.SourceUrl is not null)
        {
            existing = Playlist.FirstOrDefault(m =>
                string.Equals(m.SourceUrl, entry.SourceUrl, StringComparison.OrdinalIgnoreCase));
        }

        if (existing is not null)
        {
            SelectMedia(existing);
            return;
        }

        var item = entry.ToMediaItem(Playlist.Count + 1);
        Playlist.Insert(0, item);
        ReindexPlaylist();
        SelectMedia(item);
    }

    [RelayCommand]
    private void PlayRecentStream(RecentPlayEntry? entry)
    {
        if (entry is null) return;
        if (string.IsNullOrWhiteSpace(entry.SourceUrl))
        {
            StatusMessage = "此紀錄沒有有效網址";
            return;
        }

        NetworkUrl = entry.SourceUrl;
        PlayRecent(entry);
    }

    [RelayCommand]
    private void RemoveRecent(RecentPlayEntry? entry)
    {
        if (entry is null) return;
        _recent.Remove(entry);
        StatusMessage = $"已自最近播放移除：{entry.Title}";
    }

    [RelayCommand]
    private void RemoveRecentStream(RecentPlayEntry? entry)
    {
        if (entry is null) return;
        _streams.Remove(entry);
        StatusMessage = $"已自最近串流移除：{entry.Title}";
    }

    [RelayCommand]
    private void ClearRecent()
    {
        if (RecentItems.Count == 0)
        {
            StatusMessage = "最近播放已是空的";
            return;
        }
        _recent.Clear();
        StatusMessage = "已清除最近播放紀錄";
    }

    [RelayCommand]
    private void ClearRecentStreams()
    {
        if (RecentStreamItems.Count == 0)
        {
            StatusMessage = "最近網路串流已是空的";
            return;
        }
        _streams.Clear();
        StatusMessage = "已清除最近網路串流紀錄";
    }

    private void PlayLocalVideo(string path, string title, int generation = 0)
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
            for (var i = 0; i < 12; i++)
            {
                if (generation != 0 && generation != _selectGeneration)
                    return;
                PrepareVideoHost?.Invoke();
                if (_video.Play(path, requireVideoHost: true))
                {
                    _video.SetRate((float)PlaybackRate);
                    IsPlaying = true;
                    StatusMessage = $"正在播放影片：{title}";
                    return;
                }
                await Task.Delay(40);
            }
            if (generation != 0 && generation != _selectGeneration)
                return;
            StatusMessage = "無法嵌入影片畫面（主機控制項尚未就緒）。請再按一次播放。";
            IsPlaying = false;
        }, Avalonia.Threading.DispatcherPriority.Background);
    }

    private void PlayNetworkAudio(string url, string title)
    {
        if (StreamResolver.NeedsExtraction(url))
        {
            StatusMessage = "正在解析網路音訊（yt-dlp）…";
            _ = PlayExtractedNetworkAsync(url, title, generation: _selectGeneration, preferVideo: false);
            return;
        }

        // Audio engine needs no HWND — works even while stage is in idle/audio layout.
        if (_audio.PlayUrl(url, requireVideoHost: false))
        {
            _audio.SetRate((float)PlaybackRate);
            IsPlaying = true;
            StatusMessage = $"正在播放網路音訊：{title}";
            return;
        }

        StatusMessage = "無法開始此網路音訊。請確認網址可直接存取（http/https 媒體檔）。";
        IsPlaying = false;
    }

    private void PlayNetworkVideo(string url, string title, int generation = 0)
    {
        if (StreamResolver.NeedsExtraction(url))
        {
            StatusMessage = "正在解析 YouTube / 網頁串流（yt-dlp）…";
            _ = PlayExtractedNetworkAsync(url, title, generation == 0 ? _selectGeneration : generation, preferVideo: true);
            return;
        }

        // Prefer embedded video when host is ready; retry after stage layout creates HWND.
        PrepareVideoHost?.Invoke();
        if (_video.PlayUrl(url, requireVideoHost: true))
        {
            _video.SetRate((float)PlaybackRate);
            IsPlaying = true;
            StatusMessage = $"正在播放網路影片：{title}";
            return;
        }

        StatusMessage = "正在連線網路串流…";
        Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
        {
            for (var i = 0; i < 15; i++)
            {
                if (generation != 0 && generation != _selectGeneration)
                    return;

                PrepareVideoHost?.Invoke();
                if (_video.PlayUrl(url, requireVideoHost: true))
                {
                    _video.SetRate((float)PlaybackRate);
                    IsPlaying = true;
                    StatusMessage = $"正在播放網路影片：{title}";
                    return;
                }

                // Host still missing — try audio engine so pure-audio URLs still play.
                if (i >= 4 && _audio.PlayUrl(url, requireVideoHost: false))
                {
                    _video.PrepareForHostTeardown();
                    ActiveMediaKind = MediaKind.Audio;
                    _audio.Volume = (int)(Math.Clamp(Volume, 0, 1) * 100);
                    _audio.SetRate((float)PlaybackRate);
                    IsPlaying = true;
                    StatusMessage = $"已以音訊模式播放網路串流：{title}";
                    return;
                }

                await Task.Delay(40);
            }

            if (generation != 0 && generation != _selectGeneration)
                return;

            if (_video.HasVideoHost && _video.PlayUrl(url, requireVideoHost: true))
            {
                _video.SetRate((float)PlaybackRate);
                IsPlaying = true;
                StatusMessage = $"正在播放網路影片：{title}";
                return;
            }

            StatusMessage =
                "無法開啟網路串流。請確認為可直接播放的 http(s)/rtsp 媒體網址；YouTube 請安裝 yt-dlp 並確保可在終端機執行。";
            IsPlaying = false;
        }, Avalonia.Threading.DispatcherPriority.Background);
    }

    private async Task PlayExtractedNetworkAsync(string pageUrl, string title, int generation, bool preferVideo)
    {
        void Fail(string message)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (generation != _selectGeneration) return;
                IsPlaying = false;
                StatusMessage = message;
            });
        }

        if (!StreamResolver.IsYtDlpAvailable())
        {
            Fail("無法播放此網頁影片：需要 yt-dlp。請執行 winget install yt-dlp.yt-dlp 後重新開啟播放器。");
            return;
        }

        StreamResolver.ResolvedStream? resolved;
        try
        {
            resolved = await StreamResolver.ResolveAsync(pageUrl).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Fail($"解析串流失敗：{ex.Message}");
            return;
        }

        if (resolved is null)
        {
            Fail("yt-dlp 無法解析此網址（可能需更新：yt-dlp -U）。請確認網路與影片可公開存取。");
            return;
        }

        var display = string.IsNullOrWhiteSpace(resolved.Title) ? title : resolved.Title;

        for (var i = 0; i < 12; i++)
        {
            if (generation != _selectGeneration)
                return;

            var started = false;
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (generation != _selectGeneration)
                    return;

                WindowTitle = $"{display} — 多媒體播放器";

                if (preferVideo)
                {
                    PrepareVideoHost?.Invoke();
                    if (_video.PlayDirectUrl(resolved.PrimaryUrl, resolved.AudioUrl, requireVideoHost: true))
                    {
                        _video.SetRate((float)PlaybackRate);
                        IsPlaying = true;
                        StatusMessage = $"正在播放：{display}";
                        started = true;
                        return;
                    }

                    // After a few tries, fall back to audio path.
                    if (i >= 5 &&
                        _audio.PlayDirectUrl(resolved.PrimaryUrl, resolved.AudioUrl, requireVideoHost: false))
                    {
                        _video.PrepareForHostTeardown();
                        ActiveMediaKind = MediaKind.Audio;
                        _audio.Volume = (int)(Math.Clamp(Volume, 0, 1) * 100);
                        _audio.SetRate((float)PlaybackRate);
                        IsPlaying = true;
                        StatusMessage = $"已以音訊模式播放：{display}";
                        started = true;
                    }
                }
                else if (_audio.PlayDirectUrl(resolved.PrimaryUrl, resolved.AudioUrl, requireVideoHost: false))
                {
                    _audio.SetRate((float)PlaybackRate);
                    IsPlaying = true;
                    StatusMessage = $"正在播放網路音訊：{display}";
                    started = true;
                }
            });

            if (started)
                return;

            await Task.Delay(40).ConfigureAwait(false);
        }

        Fail($"無法播放已解析的串流：{display}");
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
            if (item.Kind == MediaKind.Audio)
                PlayNetworkAudio(item.SourceUrl, item.Title);
            else
                PlayNetworkVideo(item.SourceUrl, item.Title);
        }
    }

    [RelayCommand]
    private void StopMedia()
    {
        _audio.StopIfActive();
        _video.StopIfActive();
        IsPlaying = false;
        Progress = 0;
        PositionText = "00:00";
        StatusMessage = CurrentMedia is null ? "已停止" : $"已停止：{CurrentMedia.Title}";
    }

    [RelayCommand]
    private void PlayPrevious()
    {
        if (Playlist.Count == 0) return;
        var from = IndexOfCurrent();
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
        var from = IndexOfCurrent();
        var next = FindPlayable(from, direction: 1);
        if (next is null)
        {
            StopMedia();
            StatusMessage = "清單中沒有可播放的媒體";
            return;
        }
        SelectMedia(next);
    }

    private int IndexOfCurrent()
    {
        if (CurrentMedia is null) return -1;
        for (var i = 0; i < Playlist.Count; i++)
        {
            if (ReferenceEquals(Playlist[i], CurrentMedia))
                return i;
        }
        return -1;
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

        if (!MediaEngine.TryNormalizeStreamUri(url, out var uri))
        {
            StatusMessage = "請輸入有效的串流網址（http/https/rtsp，可省略 https://）";
            return;
        }

        NetworkUrl = uri.AbsoluteUri;

        var path = uri.AbsolutePath;
        var isAudio = MediaMetadata.IsAudio(path);
        var isVideo = MediaMetadata.IsVideo(path) || LooksLikeStreamPlaylist(path);
        var kind = isAudio && !isVideo ? MediaKind.Audio : MediaKind.Video;

        var title = StreamResolver.NeedsExtraction(uri)
            ? (uri.Host.Contains("youtu", StringComparison.OrdinalIgnoreCase) ? "YouTube 影片" : uri.Host)
            : !string.IsNullOrWhiteSpace(Path.GetFileName(path)) && path != "/"
                ? Uri.UnescapeDataString(Path.GetFileName(path))
                : uri.Host;

        // Always remember opened stream URLs (even if already in playlist).
        _streams.RecordUrl(uri.AbsoluteUri, title, kind, format: string.IsNullOrEmpty(Path.GetExtension(path))
            ? "URL"
            : Path.GetExtension(path).TrimStart('.').ToUpperInvariant());

        var existing = Playlist.FirstOrDefault(m =>
            string.Equals(m.SourceUrl, uri.AbsoluteUri, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            SelectMedia(existing);
            return;
        }

        var ext = Path.GetExtension(path);
        var item = new MediaItem
        {
            Index = Playlist.Count + 1,
            Title = title,
            Subtitle = uri.AbsoluteUri,
            Duration = "--:--",
            Kind = kind,
            SourceUrl = uri.AbsoluteUri,
            CoverHue = kind == MediaKind.Audio ? "210" : "195",
            Format = string.IsNullOrEmpty(ext) ? "URL" : ext.TrimStart('.').ToUpperInvariant()
        };
        Playlist.Insert(0, item);
        ReindexPlaylist();
        SelectMedia(item);
    }

    private static bool LooksLikeStreamPlaylist(string path)
    {
        return path.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase)
               || path.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase)
               || path.EndsWith(".mpd", StringComparison.OrdinalIgnoreCase);
    }

    [RelayCommand]
    private void ClearPlaylist()
    {
        _audio.StopIfActive();
        _video.StopIfActive();
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

    /// <summary>Dock clear button: clears the active pane (playlist / recent / streams).</summary>
    [RelayCommand]
    private void ClearDock()
    {
        switch (ActiveDockPane)
        {
            case SideDockPane.Recent:
                ClearRecent();
                break;
            case SideDockPane.Streams:
                ClearRecentStreams();
                break;
            default:
                ClearPlaylist();
                break;
        }
    }

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
            _seekTargetRatio = Math.Clamp(Progress, 0, 1);
            ActiveEngine.SeekRatio(_seekTargetRatio);
            Progress = _seekTargetRatio;
            UpdatePositionLabel(_seekTargetRatio);
            // LibVLC keeps emitting the pre-seek Time for a short window; freeze UI progress.
            _suppressTimeChangedUntilTick = Environment.TickCount64 + 500;
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

        // After EndSeek, drop stale times until demuxer catches up (common with mp3).
        if (Environment.TickCount64 < _suppressTimeChangedUntilTick)
        {
            var ratio = lengthMs > 0
                ? Math.Clamp((double)timeMs / lengthMs, 0, 1)
                : engine.GetProgressRatio();
            if (Math.Abs(ratio - _seekTargetRatio) > 0.03)
            {
                Progress = _seekTargetRatio;
                UpdatePositionLabel(_seekTargetRatio);
                return;
            }
            _suppressTimeChangedUntilTick = 0;
        }

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
