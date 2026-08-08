namespace MusicVideoMediaPlayer.Models;

/// <summary>
/// Represents a single timed lyric line parsed from an LRC file.
/// </summary>
public sealed class LrcLine
{
    /// <summary>Playback position at which this line should be highlighted (milliseconds).</summary>
    public long TimeMs { get; init; }

    /// <summary>Lyric text for this line (may be empty for instrumental gaps).</summary>
    public string Text { get; init; } = string.Empty;
}
