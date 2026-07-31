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
    private bool _isSeekingMusic;
    private bool _isSeekingVideo;
    private bool _disposed;

    /// <summary>Set by the view to open native file pickers.</summary>
    public Func<string, Task<IReadOnlyList<string>>>? PickFilesAsync { get; set; }

    /// <summary>
    /// Called by the view before video Play so LibVLC gets an embedded HWND
    /// (prevents a floating top-level video window).
    /// </summary>
    public Action? PrepareVideoHost { get; set; }

    public ObservableCollection<NavItem> LibraryNav { get; } = [];
    public ObservableCollection<PlaylistItem> Playlists { get; } = [];
    public ObservableCollection<NavItem> SettingsNav { get; } = [];
    public ObservableCollection<TrackItem> Tracks { get; } = [];
    public ObservableCollection<VideoItem> UpNextVideos { get; } = [];
    public ObservableCollection<double> WaveformBars { get; } = [];

    /// <summary>Bound to LibVLC VideoView for real video rendering.</summary>
    public MediaPlayer VideoMediaPlayer => _video.Player;

    [ObservableProperty] public partial string SearchText { get; set; } = string.Empty;
    [ObservableProperty] public partial string NetworkUrl { get; set; } = string.Empty;
    [ObservableProperty] public partial string SelectedNavId { get; set; } = "home";
    [ObservableProperty] public partial string StatusMessage { get; set; } = "就緒 — 可開啟本機音樂或影片檔案";
    [ObservableProperty] public partial TrackItem? CurrentTrack { get; set; }
    [ObservableProperty] public partial VideoItem? CurrentVideo { get; set; }
    [ObservableProperty] public partial bool IsMusicPlaying { get; set; }
    [ObservableProperty] public partial bool IsVideoPlaying { get; set; }
    [ObservableProperty] public partial bool IsFavorite { get; set; } = true;
    [ObservableProperty] public partial bool AutoPlay { get; set; } = true;
    [ObservableProperty] public partial double MusicProgress { get; set; }
    [ObservableProperty] public partial double VideoProgress { get; set; }
    [ObservableProperty] public partial double Volume { get; set; } = 1.0;
    [ObservableProperty] public partial string MusicPosition { get; set; } = "00:00";
    [ObservableProperty] public partial string MusicDuration { get; set; } = "00:00";
    [ObservableProperty] public partial string VideoPosition { get; set; } = "00:00";
    [ObservableProperty] public partial string VideoDuration { get; set; } = "00:00";
    [ObservableProperty] public partial bool HasLocalVideo { get; set; }

    public MainViewModel()
    {
        SeedNavigation();
        SeedPlaylists();
        SeedDemoTracks();
        SeedDemoVideos();
        SeedWaveform();

        CurrentTrack = Tracks.FirstOrDefault();
        CurrentVideo = CreateFeaturedVideo();
        if (CurrentTrack is not null)
            CurrentTrack.IsPlaying = true;

        _audio.Volume = (int)(Volume * 100);
        _video.Volume = (int)(Volume * 100);

        _audio.TimeChanged += OnAudioTimeChanged;
        _audio.EndReached += OnAudioEndReached;
        _audio.PlayingChanged += playing => IsMusicPlaying = playing;

        _video.TimeChanged += OnVideoTimeChanged;
        _video.EndReached += OnVideoEndReached;
        _video.PlayingChanged += playing =>
        {
            IsVideoPlaying = playing;
            HasLocalVideo = CurrentVideo?.IsLocalFile == true || CurrentVideo?.IsNetworkSource == true;
        };
    }

    partial void OnVolumeChanged(double value)
    {
        var vol = (int)(Math.Clamp(value, 0, 1) * 100);
        _audio.Volume = vol;
        _video.Volume = vol;
    }

    partial void OnMusicProgressChanged(double value)
    {
        if (_isSeekingMusic && CurrentTrack?.IsLocalFile == true)
            _audio.SeekRatio(value);
    }

    partial void OnVideoProgressChanged(double value)
    {
        if (!_isSeekingVideo || (CurrentVideo?.IsLocalFile != true && CurrentVideo?.IsNetworkSource != true))
            return;

        _video.SeekRatio(value);
        UpdateVideoPositionLabel(value);
    }

    private void SeedNavigation()
    {
        LibraryNav.Add(new NavItem { Id = "home", Label = "首頁", Icon = "⌂", IsSelected = true });
        LibraryNav.Add(new NavItem { Id = "music", Label = "音樂", Icon = "♪" });
        LibraryNav.Add(new NavItem { Id = "video", Label = "影片", Icon = "▶" });
        LibraryNav.Add(new NavItem { Id = "queue", Label = "播放清單", Icon = "☰" });
        LibraryNav.Add(new NavItem { Id = "favorites", Label = "我的最愛", Icon = "♡" });
        LibraryNav.Add(new NavItem { Id = "open", Label = "開啟檔案", Icon = "📂" });

        SettingsNav.Add(new NavItem { Id = "general", Label = "一般設定", Icon = "⚙" });
        SettingsNav.Add(new NavItem { Id = "playback", Label = "播放設定", Icon = "▷" });
        SettingsNav.Add(new NavItem { Id = "theme", Label = "外觀主題", Icon = "◐" });
        SettingsNav.Add(new NavItem { Id = "about", Label = "關於我們", Icon = "ⓘ" });
    }

    private void SeedPlaylists()
    {
        Playlists.Add(new PlaylistItem { Name = "我最愛的歌曲", Icon = "♥", Accent = "#F472B6" });
        Playlists.Add(new PlaylistItem { Name = "運動必聽", Icon = "⚡", Accent = "#34D399" });
        Playlists.Add(new PlaylistItem { Name = "睡前放鬆", Icon = "☾", Accent = "#A78BFA" });
        Playlists.Add(new PlaylistItem { Name = "經典老歌", Icon = "◆", Accent = "#FBBF24" });
    }

    private void SeedDemoTracks()
    {
        Tracks.Add(new TrackItem
        {
            Index = 1, Title = "稻香", Artist = "周杰倫", Duration = "03:43", CoverHue = "42",
            IsPlaying = true, IsFavorite = true,
            Lyrics = "對這個世界如果你有太多的抱怨\n跌倒了 就不敢繼續往前走\n為什麼 人要這麼的脆弱 墮\n請你打開電視看看"
        });
        Tracks.Add(new TrackItem { Index = 2, Title = "晴天", Artist = "周杰倫", Duration = "04:29", CoverHue = "210" });
        Tracks.Add(new TrackItem { Index = 3, Title = "夜曲", Artist = "周杰倫", Duration = "03:46", CoverHue = "280" });
        Tracks.Add(new TrackItem { Index = 4, Title = "七里香", Artist = "周杰倫", Duration = "04:58", CoverHue = "140" });
        Tracks.Add(new TrackItem { Index = 5, Title = "一路向北", Artist = "周杰倫", Duration = "04:55", CoverHue = "20" });
        Tracks.Add(new TrackItem { Index = 6, Title = "告白氣球", Artist = "周杰倫", Duration = "03:35", CoverHue = "330" });
        Tracks.Add(new TrackItem { Index = 7, Title = "不該", Artist = "周杰倫", Duration = "04:50", CoverHue = "190" });
        MusicDuration = "03:43";
    }

    private void SeedDemoVideos()
    {
        UpNextVideos.Add(new VideoItem
        {
            Title = "日本東京自由行攻略", Channel = "Travel Life", Duration = "12:45",
            Views = "觀看次數：86萬次", CoverHue = "200"
        });
        UpNextVideos.Add(new VideoItem
        {
            Title = "冰島極光之旅", Channel = "Adventure Time", Duration = "8:32",
            Views = "觀看次數：95萬次", CoverHue = "160"
        });
        UpNextVideos.Add(new VideoItem
        {
            Title = "紐約必去的10個景點", Channel = "City Walker", Duration = "15:20",
            Views = "觀看次數：72萬次", CoverHue = "230"
        });
        UpNextVideos.Add(new VideoItem
        {
            Title = "義大利美食之旅", Channel = "Foodie Diary", Duration = "10:18",
            Views = "觀看次數：65萬次", CoverHue = "30"
        });
        UpNextVideos.Add(new VideoItem
        {
            Title = "加拿大洛磯山脈健行", Channel = "Outdoor Channel", Duration = "9:45",
            Views = "觀看次數：48萬次", CoverHue = "120"
        });
    }

    private static VideoItem CreateFeaturedVideo() => new()
    {
        Title = "瑞士旅行 Vlog | 阿爾卑斯山的美景",
        Channel = "Travel Life",
        Duration = "10:30",
        Views = "觀看次數：125萬次",
        CoverHue = "195",
        Subtitle = "Travel Life · 觀看次數：125萬次 · 2024/05/20",
        Date = "2024/05/20",
        Likes = "1.2萬",
        Comments = "256"
    };

    private void SeedWaveform()
    {
        var rnd = new Random(42);
        for (var i = 0; i < 64; i++)
        {
            var envelope = Math.Sin(i / 64.0 * Math.PI);
            var noise = 0.25 + rnd.NextDouble() * 0.75;
            WaveformBars.Add(Math.Clamp(envelope * noise, 0.12, 1.0));
        }
    }

    [RelayCommand]
    private void SelectNav(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return;

        if (id == "open")
        {
            _ = OpenMediaAsync();
            return;
        }

        SelectedNavId = id;
        foreach (var item in LibraryNav)
            item.IsSelected = item.Id == id;
        foreach (var item in SettingsNav)
            item.IsSelected = item.Id == id;
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
        ImportAudioFiles(paths);
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
        ImportVideoFiles(paths);
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

        var audio = paths.Where(MediaMetadata.IsAudio).ToList();
        var video = paths.Where(MediaMetadata.IsVideo).ToList();
        var other = paths.Except(audio).Except(video).ToList();

        // Unknown extensions: treat by user intent — try as media via extension-less guess
        foreach (var p in other)
        {
            // Prefer video for common container-less paths
            if (p.Contains("video", StringComparison.OrdinalIgnoreCase))
                video.Add(p);
            else
                audio.Add(p);
        }

        if (audio.Count > 0) ImportAudioFiles(audio);
        if (video.Count > 0) ImportVideoFiles(video);
        if (audio.Count == 0 && video.Count == 0)
            StatusMessage = "未辨識到可支援的媒體格式";
    }

    public void ImportDroppedPaths(IEnumerable<string> paths)
    {
        var list = paths.Where(File.Exists).ToList();
        if (list.Count == 0) return;

        var audio = list.Where(MediaMetadata.IsAudio).ToList();
        var video = list.Where(MediaMetadata.IsVideo).ToList();
        if (audio.Count > 0) ImportAudioFiles(audio);
        if (video.Count > 0) ImportVideoFiles(video);
        if (audio.Count == 0 && video.Count == 0)
            StatusMessage = "拖放的檔案不是支援的音樂或影片格式";
    }

    private void ImportAudioFiles(IReadOnlyList<string> paths)
    {
        TrackItem? firstNew = null;
        foreach (var path in paths)
        {
            if (Tracks.Any(t => string.Equals(t.FilePath, path, StringComparison.OrdinalIgnoreCase)))
                continue;

            var info = MediaMetadata.ReadAudio(path);
            var track = new TrackItem
            {
                Index = Tracks.Count + 1,
                Title = info.Title,
                Artist = info.Artist,
                Duration = info.Duration,
                CoverHue = MediaMetadata.HueFromPath(path),
                Format = info.Format,
                Bitrate = info.Bitrate,
                Lyrics = info.Lyrics ?? path,
                FilePath = path
            };
            Tracks.Add(track);
            firstNew ??= track;
        }

        ReindexTracks();
        StatusMessage = $"已加入 {paths.Count} 首本機音樂";

        if (firstNew is not null)
            SelectTrack(firstNew);
    }

    private void ImportVideoFiles(IReadOnlyList<string> paths)
    {
        VideoItem? firstNew = null;
        foreach (var path in paths)
        {
            if (UpNextVideos.Any(v => string.Equals(v.FilePath, path, StringComparison.OrdinalIgnoreCase)))
                continue;

            var info = MediaMetadata.ReadVideo(path);
            var item = new VideoItem
            {
                Title = info.Title,
                Channel = info.Channel,
                Duration = info.Duration,
                Views = Path.GetDirectoryName(path) ?? "本機",
                CoverHue = MediaMetadata.HueFromPath(path),
                Subtitle = $"{info.Channel} · {info.Format} · {info.Duration}",
                Likes = "—",
                Comments = "—",
                FilePath = path
            };
            // Insert local files at top of up-next
            UpNextVideos.Insert(0, item);
            firstNew ??= item;
        }

        StatusMessage = $"已加入 {paths.Count} 部本機影片";
        if (firstNew is not null)
            SelectVideo(firstNew);
    }

    [RelayCommand]
    private void PlayNetworkUrl()
    {
        var url = NetworkUrl.Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            StatusMessage = "Please enter a valid http:// or https:// media URL.";
            return;
        }

        var title = uri.Host.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) ||
                    uri.Host.Contains("youtu.be", StringComparison.OrdinalIgnoreCase)
            ? "YouTube video"
            : uri.Host;
        var item = new VideoItem
        {
            Title = title,
            Channel = uri.Host,
            Duration = "--:--",
            Views = "Network source",
            CoverHue = "195",
            Subtitle = uri.AbsoluteUri,
            SourceUrl = uri.AbsoluteUri
        };

        if (!UpNextVideos.Any(v => string.Equals(v.SourceUrl, item.SourceUrl, StringComparison.OrdinalIgnoreCase)))
            UpNextVideos.Insert(0, item);

        SelectVideo(item);
    }

    private void ReindexTracks()
    {
        for (var i = 0; i < Tracks.Count; i++)
            Tracks[i].Index = i + 1;
    }

    [RelayCommand]
    private void SelectTrack(TrackItem? track)
    {
        if (track is null) return;
        foreach (var t in Tracks)
            t.IsPlaying = false;
        track.IsPlaying = true;
        CurrentTrack = track;
        MusicDuration = track.Duration;
        MusicPosition = "00:00";
        MusicProgress = 0;
        IsFavorite = track.IsFavorite;

        if (track.IsLocalFile && track.FilePath is not null)
        {
            _video.Pause();
            _audio.Play(track.FilePath);
            IsMusicPlaying = true;
            StatusMessage = $"正在播放：{track.Title}";
        }
        else
        {
            _audio.Stop();
            IsMusicPlaying = true; // demo visual state
            StatusMessage = $"示範曲目（無檔案）：{track.Title} — 請用「開啟音樂」載入本機檔案";
        }
    }

    [RelayCommand]
    private void SelectVideo(VideoItem? video)
    {
        if (video is null) return;
        CurrentVideo = video with
        {
            Subtitle = string.IsNullOrEmpty(video.Subtitle)
                ? $"{video.Channel} · {video.Views}"
                : video.Subtitle,
            Likes = video.Likes ?? "—",
            Comments = video.Comments ?? "—"
        };
        VideoDuration = video.Duration;
        VideoPosition = "00:00";
        VideoProgress = 0;
        HasLocalVideo = video.IsLocalFile || video.IsNetworkSource;

        if (video.IsLocalFile && video.FilePath is not null)
        {
            _audio.Pause();
            // Must attach HWND first; otherwise LibVLC opens a separate OS window.
            PlayLocalVideo(video.FilePath, video.Title);
        }
        else if (video.IsNetworkSource && video.SourceUrl is not null)
        {
            _audio.Pause();
            PlayNetworkVideo(video.SourceUrl, video.Title);
        }
        else
        {
            _video.Stop();
            IsVideoPlaying = false;
            StatusMessage = $"示範影片（無檔案）：{video.Title} — 請用「開啟影片」載入本機檔案";
        }
    }

    private void PlayLocalVideo(string path, string title)
    {
        PrepareVideoHost?.Invoke();

        if (_video.Play(path, requireVideoHost: true))
        {
            IsVideoPlaying = true;
            HasLocalVideo = true;
            StatusMessage = $"正在播放影片：{title}";
            return;
        }

        // Host may not be ready on first frame — retry shortly on UI thread.
        Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
        {
            for (var i = 0; i < 10; i++)
            {
                PrepareVideoHost?.Invoke();
                if (_video.Play(path, requireVideoHost: true))
                {
                    IsVideoPlaying = true;
                    HasLocalVideo = true;
                    StatusMessage = $"正在播放影片：{title}";
                    return;
                }

                await Task.Delay(50);
            }

            StatusMessage = "無法嵌入影片畫面（主機控制項尚未就緒）。請再按一次播放。";
            IsVideoPlaying = false;
        }, Avalonia.Threading.DispatcherPriority.Background);
    }

    private void PlayNetworkVideo(string url, string title)
    {
        PrepareVideoHost?.Invoke();
        if (_video.PlayUrl(url, requireVideoHost: true))
        {
            IsVideoPlaying = true;
            HasLocalVideo = true;
            StatusMessage = $"Playing network video: {title}";
            return;
        }

        StatusMessage = "Unable to start this network video. Check the URL or LibVLC site support.";
        IsVideoPlaying = false;
    }

    [RelayCommand]
    private void ToggleMusicPlay()
    {
        if (CurrentTrack?.IsLocalFile == true)
        {
            if (_audio.IsPlaying)
            {
                _audio.Pause();
                IsMusicPlaying = false;
            }
            else if (_audio.Player.Media is not null)
            {
                _audio.Player.Play();
                IsMusicPlaying = true;
            }
            else if (CurrentTrack.FilePath is not null)
            {
                _audio.Play(CurrentTrack.FilePath);
                IsMusicPlaying = true;
            }
        }
        else
        {
            IsMusicPlaying = !IsMusicPlaying;
            if (!IsMusicPlaying)
                StatusMessage = "示範曲目已暫停（無實際音訊）";
        }
    }

    [RelayCommand]
    private void ToggleVideoPlay()
    {
        if (CurrentVideo?.IsLocalFile == true || CurrentVideo?.IsNetworkSource == true)
        {
            if (_video.IsPlaying)
            {
                _video.Pause();
                IsVideoPlaying = false;
            }
            else if (_video.Player.Media is not null && _video.HasVideoHost)
            {
                PrepareVideoHost?.Invoke();
                _video.Player.Play();
                IsVideoPlaying = true;
            }
            else if (CurrentVideo.FilePath is not null)
            {
                PlayLocalVideo(CurrentVideo.FilePath, CurrentVideo.Title);
            }
            else if (CurrentVideo.SourceUrl is not null)
            {
                PlayNetworkVideo(CurrentVideo.SourceUrl, CurrentVideo.Title);
            }
        }
        else
        {
            IsVideoPlaying = !IsVideoPlaying;
            if (IsVideoPlaying)
                StatusMessage = "示範影片無法實際播放 — 請開啟本機影片檔案";
        }
    }

    [RelayCommand]
    private void ToggleFavorite()
    {
        IsFavorite = !IsFavorite;
        if (CurrentTrack is not null)
            CurrentTrack.IsFavorite = IsFavorite;
    }

    [RelayCommand]
    private void ClearQueue()
    {
        _audio.Stop();
        var demoOnly = Tracks.Where(t => !t.IsLocalFile).ToList();
        Tracks.Clear();
        foreach (var t in demoOnly)
            Tracks.Add(t);
        ReindexTracks();
        if (Tracks.Count > 0)
            SelectTrack(Tracks[0]);
        else
        {
            CurrentTrack = null;
            IsMusicPlaying = false;
        }
        StatusMessage = "已清除本機音樂佇列";
    }

    [RelayCommand]
    private void PlayPrevious()
    {
        if (CurrentTrack is null || Tracks.Count == 0) return;
        var idx = Tracks.ToList().FindIndex(t => ReferenceEquals(t, CurrentTrack) || t.Index == CurrentTrack.Index);
        var prev = idx <= 0 ? Tracks[^1] : Tracks[idx - 1];
        SelectTrack(prev);
    }

    [RelayCommand]
    private void PlayNext()
    {
        if (CurrentTrack is null || Tracks.Count == 0) return;
        var idx = Tracks.ToList().FindIndex(t => ReferenceEquals(t, CurrentTrack) || t.Index == CurrentTrack.Index);
        var next = idx < 0 || idx >= Tracks.Count - 1 ? Tracks[0] : Tracks[idx + 1];
        SelectTrack(next);
    }

    public void BeginMusicSeek() => _isSeekingMusic = true;

    public void EndMusicSeek()
    {
        if (_isSeekingMusic && CurrentTrack?.IsLocalFile == true)
            _audio.SeekRatio(MusicProgress);
        _isSeekingMusic = false;
    }

    public void BeginVideoSeek() => _isSeekingVideo = true;

    public void EndVideoSeek()
    {
        // Commit final scrub position (click-or-drag on timeline).
        if (_isSeekingVideo && (CurrentVideo?.IsLocalFile == true || CurrentVideo?.IsNetworkSource == true))
        {
            _video.SeekRatio(VideoProgress);
            UpdateVideoPositionLabel(VideoProgress);
        }
        _isSeekingVideo = false;
    }

    [RelayCommand]
    private void SeekVideoRelative(object? parameter)
    {
        if (CurrentVideo?.IsLocalFile != true && CurrentVideo?.IsNetworkSource != true)
            return;

        var seconds = 10.0;
        if (parameter is int i) seconds = i;
        else if (parameter is string s && double.TryParse(s, out var d)) seconds = d;
        else if (parameter is double dd) seconds = dd;

        _video.SeekBySeconds(seconds);
        VideoProgress = _video.GetProgressRatio();
        UpdateVideoPositionLabel(VideoProgress);
    }

    private void UpdateVideoPositionLabel(double ratio)
    {
        ratio = Math.Clamp(ratio, 0, 1);
        var lengthMs = _video.Length;
        if (lengthMs > 0)
        {
            var timeMs = (long)(lengthMs * ratio);
            VideoPosition = MediaMetadata.FormatDuration(TimeSpan.FromMilliseconds(timeMs));
            VideoDuration = MediaMetadata.FormatDuration(TimeSpan.FromMilliseconds(lengthMs));
        }
    }

    private void OnAudioTimeChanged(long timeMs, long lengthMs)
    {
        if (_isSeekingMusic) return;
        if (lengthMs > 0)
        {
            MusicProgress = (double)timeMs / lengthMs;
            MusicDuration = MediaMetadata.FormatDuration(TimeSpan.FromMilliseconds(lengthMs));
        }
        MusicPosition = MediaMetadata.FormatDuration(TimeSpan.FromMilliseconds(timeMs));
    }

    private void OnVideoTimeChanged(long timeMs, long lengthMs)
    {
        // Don't fight the user while they are scrubbing the timeline.
        if (_isSeekingVideo) return;

        // Network sources (especially YouTube) may expose a fractional position
        // before LibVLC has discovered their duration.  Using Position here keeps
        // the timeline responsive instead of leaving it fixed at the beginning.
        VideoProgress = _video.GetProgressRatio();
        if (lengthMs > 0)
        {
            VideoDuration = MediaMetadata.FormatDuration(TimeSpan.FromMilliseconds(lengthMs));
        }
        VideoPosition = MediaMetadata.FormatDuration(TimeSpan.FromMilliseconds(timeMs));
    }

    private void OnAudioEndReached()
    {
        if (AutoPlay)
            PlayNext();
        else
            IsMusicPlaying = false;
    }

    private void OnVideoEndReached()
    {
        IsVideoPlaying = false;
        if (!AutoPlay) return;

        var locals = UpNextVideos.Where(v => v.IsLocalFile).ToList();
        if (locals.Count == 0) return;
        var idx = locals.FindIndex(v => v.FilePath == CurrentVideo?.FilePath);
        var next = idx < 0 || idx >= locals.Count - 1 ? locals[0] : locals[idx + 1];
        SelectVideo(next);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _audio.Dispose();
        _video.Dispose();
    }
}
