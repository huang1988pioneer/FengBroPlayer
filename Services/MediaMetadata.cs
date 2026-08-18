using System;
using System.IO;
using System.Linq;

namespace FengBroPlayer.Services;

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

    public static readonly string[] SubtitleExtensions =
    [
        ".srt", ".ass", ".ssa", ".vtt", ".sub"
    ];

    /// <summary>
    /// Looks for a subtitle file beside <paramref name="videoPath"/> with the same stem.
    /// Checks each <see cref="SubtitleExtensions"/> in order; returns the first match or null.
    /// Example: "movie.mp4" → "movie.srt" or "movie.ass"
    /// </summary>
    public static string? FindSidecarSubtitle(string videoPath)
    {
        if (string.IsNullOrWhiteSpace(videoPath))
            return null;
        var dir = Path.GetDirectoryName(videoPath);
        var stem = Path.GetFileNameWithoutExtension(videoPath);
        if (string.IsNullOrWhiteSpace(dir) || string.IsNullOrWhiteSpace(stem))
            return null;

        foreach (var ext in SubtitleExtensions)
        {
            var candidate = Path.Combine(dir, stem + ext);
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }

    private static readonly string[] CoverFileNames =
    [
        "cover.jpg", "cover.jpeg", "cover.png", "cover.webp",
        "folder.jpg", "folder.jpeg", "folder.png",
        "album.jpg", "album.jpeg", "album.png",
        "AlbumArt.jpg", "AlbumArtSmall.jpg", "front.jpg", "Front.jpg"
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
        TimeSpan Length,
        byte[]? CoverArt = null);

    public sealed record VideoInfo(
        string Title,
        string Channel,
        string Duration,
        string Format,
        TimeSpan Length,
        int Width = 0,
        int Height = 0,
        string VideoCodec = "",
        string AudioCodec = "");

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

            var cover = ExtractEmbeddedCover(file) ?? TryLoadSidecarCover(path);

            return new AudioInfo(
                title,
                artist,
                FormatDuration(length),
                string.IsNullOrEmpty(ext) ? "AUDIO" : ext,
                bitrate,
                lyrics,
                length,
                cover);
        }
        catch
        {
            return new AudioInfo(name, "本機檔案", "—:—", ext, "—", path, TimeSpan.Zero, TryLoadSidecarCover(path));
        }
    }

    /// <summary>Extract first embedded picture (album art) from TagLib tags.</summary>
    public static byte[]? ExtractEmbeddedCover(TagLib.File file)
    {
        try
        {
            var pictures = file.Tag.Pictures;
            if (pictures is null || pictures.Length == 0)
                return null;

            // Prefer front cover; skip tiny file icons that often look blank.
            var preferred = pictures
                .Where(p => p.Data is { Count: > 256 })
                .OrderBy(p => p.Type is TagLib.PictureType.FrontCover ? 0
                    : p.Type is TagLib.PictureType.Other ? 1
                    : p.Type is TagLib.PictureType.FileIcon or TagLib.PictureType.OtherFileIcon ? 9
                    : 2)
                .FirstOrDefault();

            if (preferred?.Data is null || preferred.Data.Count < 256)
                return null;

            return preferred.Data.Data;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Load cover.jpg / folder.jpg next to the media file.</summary>
    public static byte[]? TryLoadSidecarCover(string mediaPath)
    {
        try
        {
            var dir = Path.GetDirectoryName(mediaPath);
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
                return null;

            foreach (var name in CoverFileNames)
            {
                var candidate = Path.Combine(dir, name);
                if (!File.Exists(candidate))
                    continue;
                var bytes = File.ReadAllBytes(candidate);
                if (bytes.Length > 0)
                    return bytes;
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    /// <summary>Load cover art bytes for a local audio file (embedded or sidecar).</summary>
    public static byte[]? LoadCoverArtBytes(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        try
        {
            using var file = TagLib.File.Create(path);
            var embedded = ExtractEmbeddedCover(file);
            if (embedded is { Length: > 0 })
                return embedded;
        }
        catch
        {
            // fall through to sidecar
        }

        return TryLoadSidecarCover(path);
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
            var codecs = file.Properties.Codecs.ToArray();
            var videoCodec = codecs.FirstOrDefault(c => (c.MediaTypes & TagLib.MediaTypes.Video) != 0)?.Description ?? "";
            var audioCodec = codecs.FirstOrDefault(c => (c.MediaTypes & TagLib.MediaTypes.Audio) != 0)?.Description ?? "";
            return new VideoInfo(
                title,
                "本機影片",
                FormatDuration(length),
                string.IsNullOrEmpty(ext) ? "VIDEO" : ext,
                length,
                file.Properties.VideoWidth,
                file.Properties.VideoHeight,
                videoCodec,
                audioCodec);
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
