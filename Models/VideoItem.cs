namespace FengBroPlayer33.Models;

public sealed record VideoItem
{
    public required string Title { get; init; }
    public required string Channel { get; init; }
    public required string Duration { get; init; }
    public required string Views { get; init; }
    public required string CoverHue { get; init; }
    public string? Subtitle { get; init; }
    public string? Date { get; init; }
    public string? Likes { get; init; }
    public string? Comments { get; init; }
    public string? FilePath { get; init; }
    public string? SourceUrl { get; init; }
    public bool IsLocalFile => !string.IsNullOrWhiteSpace(FilePath);
    public bool IsNetworkSource => !string.IsNullOrWhiteSpace(SourceUrl);
}
