namespace MusicVideoMediaPlayer.Models;

public enum MediaKind
{
    None,
    Audio,
    Video
}

public enum ChromeMode
{
    Normal,
    Fullscreen,
    Compact
}

/// <summary>Right-hand dock tab: playlist, general recent, or network streams.</summary>
public enum SideDockPane
{
    Playlist = 0,
    Recent = 1,
    Streams = 2
}
