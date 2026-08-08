using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using MusicVideoMediaPlayer.Models;

namespace MusicVideoMediaPlayer.Services;

/// <summary>
/// Persists recently played media under LocalApplicationData.
/// Newest entries first; de-duplicated by file path or URL.
/// </summary>
public sealed class RecentPlayStore
{
    public const int MaxEntries = 50;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _filePath;
    private bool _loaded;

    public ObservableCollection<RecentPlayEntry> Items { get; } = [];

    public RecentPlayStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MusicVideoMediaPlayer");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "recent.json");
    }

    public void Load()
    {
        if (_loaded) return;
        _loaded = true;

        try
        {
            if (!File.Exists(_filePath))
                return;

            var json = File.ReadAllText(_filePath);
            var dto = JsonSerializer.Deserialize<RecentFileDto>(json, JsonOptions);
            if (dto?.Entries is null || dto.Entries.Count == 0)
                return;

            Items.Clear();
            foreach (var e in dto.Entries.Take(MaxEntries))
            {
                if (string.IsNullOrWhiteSpace(e.Title))
                    continue;
                if (string.IsNullOrWhiteSpace(e.FilePath) && string.IsNullOrWhiteSpace(e.SourceUrl))
                    continue;

                Items.Add(new RecentPlayEntry
                {
                    Title = e.Title,
                    Subtitle = e.Subtitle ?? "",
                    FilePath = e.FilePath,
                    SourceUrl = e.SourceUrl,
                    Kind = e.Kind is "Video" or "video" ? MediaKind.Video
                        : e.Kind is "Audio" or "audio" ? MediaKind.Audio
                        : MediaKind.None,
                    Duration = string.IsNullOrWhiteSpace(e.Duration) ? "--:--" : e.Duration,
                    Format = e.Format ?? "",
                    CoverHue = string.IsNullOrWhiteSpace(e.CoverHue) ? "200" : e.CoverHue,
                    Bitrate = e.Bitrate ?? "",
                    PlayedAtUtc = e.PlayedAtUtc == default ? DateTime.UtcNow : e.PlayedAtUtc
                });
            }
        }
        catch
        {
            // Corrupt file — start empty; next Save rewrites.
            Items.Clear();
        }
    }

    /// <summary>Insert or move to top. Returns false if item is not recordable.</summary>
    public bool Record(MediaItem item)
    {
        if (item.IsPlayable != true)
            return false;
        if (string.IsNullOrWhiteSpace(item.FilePath) && string.IsNullOrWhiteSpace(item.SourceUrl))
            return false;

        Load();

        var key = !string.IsNullOrWhiteSpace(item.FilePath)
            ? "file:" + item.FilePath.Trim()
            : "url:" + item.SourceUrl!.Trim();

        for (var i = Items.Count - 1; i >= 0; i--)
        {
            if (string.Equals(Items[i].Key, key, StringComparison.OrdinalIgnoreCase))
                Items.RemoveAt(i);
        }

        Items.Insert(0, RecentPlayEntry.FromMedia(item, DateTime.UtcNow));

        while (Items.Count > MaxEntries)
            Items.RemoveAt(Items.Count - 1);

        Save();
        return true;
    }

    public void Remove(RecentPlayEntry entry)
    {
        Load();
        if (Items.Remove(entry))
            Save();
    }

    public void Clear()
    {
        Load();
        if (Items.Count == 0) return;
        Items.Clear();
        Save();
    }

    public void Save()
    {
        try
        {
            var dto = new RecentFileDto
            {
                Entries = Items.Select(e => new RecentEntryDto
                {
                    Title = e.Title,
                    Subtitle = e.Subtitle,
                    FilePath = e.FilePath,
                    SourceUrl = e.SourceUrl,
                    Kind = e.Kind.ToString(),
                    Duration = e.Duration,
                    Format = e.Format,
                    CoverHue = e.CoverHue,
                    Bitrate = e.Bitrate,
                    PlayedAtUtc = e.PlayedAtUtc
                }).ToList()
            };

            var json = JsonSerializer.Serialize(dto, JsonOptions);
            File.WriteAllText(_filePath, json);
        }
        catch
        {
            // Disk full / permissions — ignore; history is best-effort.
        }
    }

    private sealed class RecentFileDto
    {
        public List<RecentEntryDto> Entries { get; set; } = [];
    }

    private sealed class RecentEntryDto
    {
        public string Title { get; set; } = "";
        public string? Subtitle { get; set; }
        public string? FilePath { get; set; }
        public string? SourceUrl { get; set; }
        public string? Kind { get; set; }
        public string? Duration { get; set; }
        public string? Format { get; set; }
        public string? CoverHue { get; set; }
        public string? Bitrate { get; set; }
        public DateTime PlayedAtUtc { get; set; }
    }
}
