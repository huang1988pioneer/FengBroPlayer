using CommunityToolkit.Mvvm.ComponentModel;

namespace FengBroPlayer.Models;

public partial class TrackItem : ObservableObject
{
    public required int Index { get; set; }
    public required string Title { get; init; }
    public required string Artist { get; init; }
    public required string Duration { get; init; }
    public required string CoverHue { get; init; }
    public string Format { get; init; } = "MP3";
    public string Bitrate { get; init; } = "320kbps";
    public string Lyrics { get; init; } = string.Empty;
    public string? FilePath { get; init; }
    public bool IsLocalFile => !string.IsNullOrWhiteSpace(FilePath);

    [ObservableProperty]
    public partial bool IsPlaying { get; set; }

    [ObservableProperty]
    public partial bool IsFavorite { get; set; }
}
