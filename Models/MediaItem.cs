using CommunityToolkit.Mvvm.ComponentModel;

namespace MusicVideoMediaPlayer.Models;

public partial class MediaItem : ObservableObject
{
    public required int Index { get; set; }

    /// <summary>Display title — updated when network metadata (yt-dlp / LibVLC) arrives.</summary>
    [ObservableProperty]
    public partial string Title { get; set; } = "";

    [ObservableProperty]
    public partial string Subtitle { get; set; } = "";

    [ObservableProperty]
    public partial string Duration { get; set; } = "--:--";

    public required MediaKind Kind { get; init; }
    public string? FilePath { get; init; }
    public string? SourceUrl { get; init; }
    public string CoverHue { get; init; } = "200";

    [ObservableProperty]
    public partial string Format { get; set; } = "";

    public string Bitrate { get; init; } = "";
    public int VideoWidth { get; init; }
    public int VideoHeight { get; init; }

    public bool IsLocalFile => !string.IsNullOrWhiteSpace(FilePath);
    public bool IsNetworkSource => !string.IsNullOrWhiteSpace(SourceUrl);
    public bool IsPlayable => IsLocalFile || IsNetworkSource;

    [ObservableProperty]
    public partial bool IsCurrent { get; set; }
}
