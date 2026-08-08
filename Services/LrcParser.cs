using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using MusicVideoMediaPlayer.Models;

namespace MusicVideoMediaPlayer.Services;

/// <summary>Parses standard timestamped LRC files, including multiple timestamps per line.</summary>
public static partial class LrcParser
{
    private static readonly Regex Timestamp = TimestampRegex();

    public static IReadOnlyList<LrcLine> Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return Array.Empty<LrcLine>();
        var result = new List<LrcLine>();
        var offsetMs = 0L;
        foreach (var raw in File.ReadLines(path, DetectEncoding(path)))
        {
            var offsetMatch = OffsetRegex().Match(raw);
            if (offsetMatch.Success && long.TryParse(offsetMatch.Groups[1].Value, out var parsedOffset))
            {
                offsetMs = parsedOffset;
                continue;
            }
            var matches = Timestamp.Matches(raw);
            if (matches.Count == 0) continue;
            var text = Timestamp.Replace(raw, string.Empty).Trim();
            foreach (Match match in matches)
            {
                if (!int.TryParse(match.Groups[1].Value, out var minutes)
                    || !double.TryParse(match.Groups[2].Value, NumberStyles.AllowDecimalPoint,
                        CultureInfo.InvariantCulture, out var seconds)) continue;
                result.Add(new LrcLine { TimeMs = Math.Max(0, (long)Math.Round((minutes * 60 + seconds) * 1000) + offsetMs), Text = text });
            }
        }
        result.Sort((a, b) => a.TimeMs.CompareTo(b.TimeMs));
        return result;
    }

    public static string? FindSidecar(string mediaPath)
    {
        var directory = Path.GetDirectoryName(mediaPath);
        var stem = Path.GetFileNameWithoutExtension(mediaPath);
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(stem)) return null;
        var candidate = Path.Combine(directory, stem + ".lrc");
        return File.Exists(candidate) ? candidate : null;
    }

    private static Encoding DetectEncoding(string path)
    {
        using var stream = File.OpenRead(path);
        Span<byte> bom = stackalloc byte[3];
        var read = stream.Read(bom);
        if (read >= 2 && bom[0] == 0xff && bom[1] == 0xfe) return Encoding.Unicode;
        if (read >= 2 && bom[0] == 0xfe && bom[1] == 0xff) return Encoding.BigEndianUnicode;
        return new UTF8Encoding(false, false);
    }

    [GeneratedRegex(@"\[(\d{1,3}):(\d{1,2}(?:\.\d{1,3})?)\]")]
    private static partial Regex TimestampRegex();
    [GeneratedRegex(@"^\[offset:([+-]?\d+)\]$", RegexOptions.IgnoreCase)]
    private static partial Regex OffsetRegex();
}
