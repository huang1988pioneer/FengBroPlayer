using CommunityToolkit.Mvvm.ComponentModel;

namespace MusicVideoMediaPlayer.Models;

public partial class MediaItem : ObservableObject
{
    public required int Index { get; set; }
    public required string Title { get; init; }
    public string Subtitle { get; init; } = "";
    public required string Duration { get; init; }
    public required MediaKind Kind { get; init; }
    public string? FilePath { get; init; }
    public string? SourceUrl { get; init; }
    public string CoverHue { get; init; } = "200";
    public string Format { get; init; } = "";
    public string Bitrate { get; init; } = "";
    public int VideoWidth { get; init; }
    public int VideoHeight { get; init; }

    public bool IsLocalFile => !string.IsNullOrWhiteSpace(FilePath);
    public bool IsNetworkSource => !string.IsNullOrWhiteSpace(SourceUrl);
    public bool IsPlayable => IsLocalFile || IsNetworkSource;

    [ObservableProperty]
    public partial bool IsCurrent { get; set; }
}
