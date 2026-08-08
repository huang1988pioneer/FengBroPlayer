using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MusicVideoMediaPlayer.Models;

/// <summary>One row in the recently-played list (in-memory + JSON persistence).</summary>
public partial class RecentPlayEntry : ObservableObject
{
    public required string Title { get; init; }
    public string Subtitle { get; init; } = "";
    public string? FilePath { get; init; }
    public string? SourceUrl { get; init; }
    public MediaKind Kind { get; init; } = MediaKind.None;
    public string Duration { get; init; } = "--:--";
    public string Format { get; init; } = "";
    public string CoverHue { get; init; } = "200";
    public string Bitrate { get; init; } = "";
    public DateTime PlayedAtUtc { get; set; }

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
