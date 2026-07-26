using System;
using System.IO;

namespace MusicVideoMediaPlayer.Services;

public static class MediaMetadata
{
    public static readonly string[] AudioExtensions =
    [
        ".mp3", ".flac", ".wav", ".m4a", ".aac", ".ogg", ".wma", ".aiff", ".opus"
    ];

    public static readonly string[] VideoExtensions =
    [
        ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm", ".m4v", ".ts", ".flv"
    ];

    public static bool IsAudio(string path)
        => Array.Exists(AudioExtensions, e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase));

    public static bool IsVideo(string path)
        => Array.Exists(VideoExtensions, e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase));

    public sealed record AudioInfo(
        string Title,
        string Artist,
        string Duration,
        string Format,
        string Bitrate,
        string? Lyrics,
        TimeSpan Length);

    public sealed record VideoInfo(
        string Title,
        string Channel,
        string Duration,
        string Format,
        TimeSpan Length);

    public static AudioInfo ReadAudio(string path)
    {
        var ext = Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
        var name = Path.GetFileNameWithoutExtension(path);
        try
        {
            using var file = TagLib.File.Create(path);
            var title = string.IsNullOrWhiteSpace(file.Tag.Title) ? name : file.Tag.Title;
            var artist = file.Tag.FirstPerformer
                         ?? file.Tag.FirstAlbumArtist
                         ?? "本機檔案";
            var length = file.Properties.Duration;
            var bitrate = file.Properties.AudioBitrate > 0
                ? $"{file.Properties.AudioBitrate}kbps"
                : "—";
            var lyrics = file.Tag.Lyrics;
            if (string.IsNullOrWhiteSpace(lyrics))
                lyrics = $"檔案位置\n{path}";

            return new AudioInfo(
                title,
                artist,
                FormatDuration(length),
                string.IsNullOrEmpty(ext) ? "AUDIO" : ext,
                bitrate,
                lyrics,
                length);
        }
        catch
        {
            return new AudioInfo(name, "本機檔案", "—:—", ext, "—", path, TimeSpan.Zero);
        }
    }

    public static VideoInfo ReadVideo(string path)
    {
        var ext = Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
        var name = Path.GetFileNameWithoutExtension(path);
        try
        {
            using var file = TagLib.File.Create(path);
            var title = string.IsNullOrWhiteSpace(file.Tag.Title) ? name : file.Tag.Title;
            var length = file.Properties.Duration;
            return new VideoInfo(
                title,
                "本機影片",
                FormatDuration(length),
                string.IsNullOrEmpty(ext) ? "VIDEO" : ext,
                length);
        }
        catch
        {
            return new VideoInfo(name, "本機影片", "—:—", ext, TimeSpan.Zero);
        }
    }

    public static string FormatDuration(TimeSpan ts)
    {
        if (ts <= TimeSpan.Zero) return "00:00";
        return ts.TotalHours >= 1
            ? ts.ToString(@"h\:mm\:ss")
            : ts.ToString(@"mm\:ss");
    }

    public static string HueFromPath(string path)
    {
        unchecked
        {
            var hash = path.GetHashCode();
            return Math.Abs(hash % 360).ToString();
        }
    }
}
