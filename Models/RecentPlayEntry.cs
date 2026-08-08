using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MusicVideoMediaPlayer.Models;

/// <summary>One row in the recently-played list (in-memory + JSON persistence).</summary>
public partial class RecentPlayEntry : ObservableObject
{
    [ObservableProperty]
    public partial string Title { get; set; } = "";

    [ObservableProperty]
    public partial string Subtitle { get; set; } = "";

    public string? FilePath { get; init; }
    public string? SourceUrl { get; init; }
    public MediaKind Kind { get; init; } = MediaKind.None;

    [ObservableProperty]
    public partial string Duration { get; set; } = "--:--";

    [ObservableProperty]
    public partial string Format { get; set; } = "";

    public string CoverHue { get; init; } = "200";
    public string Bitrate { get; init; } = "";

    [ObservableProperty]
    public partial DateTime PlayedAtUtc { get; set; }

    public bool IsLocalFile => !string.IsNullOrWhiteSpace(FilePath);
    public bool IsNetworkSource => !string.IsNullOrWhiteSpace(SourceUrl);

    /// <summary>Stable identity for de-duplication.</summary>
    public string Key
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(FilePath))
                return "file:" + FilePath.Trim();
            if (!string.IsNullOrWhiteSpace(SourceUrl))
                return "url:" + SourceUrl.Trim();
            return "title:" + Title;
        }
    }

    public string KindLabel => Kind == MediaKind.Video ? "影片" : Kind == MediaKind.Audio ? "音樂" : "";

    public string PlayedAtText
    {
        get
        {
            var local = PlayedAtUtc.ToLocalTime();
            var today = DateTime.Today;
            if (local.Date == today)
                return local.ToString("HH:mm");
            if (local.Date == today.AddDays(-1))
                return "昨天 " + local.ToString("HH:mm");
            if (local.Year == today.Year)
                return local.ToString("M/d HH:mm");
            return local.ToString("yyyy/M/d");
        }
    }

    partial void OnPlayedAtUtcChanged(DateTime value) => OnPropertyChanged(nameof(PlayedAtText));

    public static RecentPlayEntry FromMedia(MediaItem item, DateTime utcNow)
        => new()
        {
            Title = item.Title,
            Subtitle = item.Subtitle,
            FilePath = item.FilePath,
            SourceUrl = item.SourceUrl,
            Kind = item.Kind,
            Duration = item.Duration,
            Format = item.Format,
            CoverHue = item.CoverHue,
            Bitrate = item.Bitrate,
            PlayedAtUtc = utcNow
        };

    public MediaItem ToMediaItem(int index)
        => new()
        {
            Index = index,
            Title = Title,
            Subtitle = Subtitle,
            Duration = Duration,
            Kind = Kind,
            FilePath = FilePath,
            SourceUrl = SourceUrl,
            CoverHue = CoverHue,
            Format = Format,
            Bitrate = Bitrate
        };
}
