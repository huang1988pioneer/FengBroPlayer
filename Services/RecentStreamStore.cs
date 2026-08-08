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
/// Persists recently opened network stream URLs (separate from local recent play).
/// Stored under LocalApplicationData as recent-streams.json.
/// </summary>
public sealed class RecentStreamStore
{
    public const int MaxEntries = 30;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _filePath;
    private bool _loaded;

    public ObservableCollection<RecentPlayEntry> Items { get; } = [];

    public RecentStreamStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MusicVideoMediaPlayer");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "recent-streams.json");
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
            var dto = JsonSerializer.Deserialize<StreamFileDto>(json, JsonOptions);
            if (dto?.Entries is null || dto.Entries.Count == 0)
                return;

            Items.Clear();
            foreach (var e in dto.Entries.Take(MaxEntries))
            {
                if (string.IsNullOrWhiteSpace(e.SourceUrl))
                    continue;
                if (!MediaEngine.TryNormalizeStreamUri(e.SourceUrl, out var uri))
                    continue;

                Items.Add(new RecentPlayEntry
                {
                    Title = string.IsNullOrWhiteSpace(e.Title) ? uri.Host : e.Title,
                    Subtitle = uri.AbsoluteUri,
                    SourceUrl = uri.AbsoluteUri,
                    Kind = e.Kind is "Audio" or "audio" ? MediaKind.Audio : MediaKind.Video,
                    Duration = string.IsNullOrWhiteSpace(e.Duration) ? "--:--" : e.Duration,
                    Format = string.IsNullOrWhiteSpace(e.Format) ? "URL" : e.Format,
                    CoverHue = string.IsNullOrWhiteSpace(e.CoverHue) ? "195" : e.CoverHue,
                    Bitrate = e.Bitrate ?? "",
                    PlayedAtUtc = e.PlayedAtUtc == default ? DateTime.UtcNow : e.PlayedAtUtc
                });
            }
        }
        catch
        {
            Items.Clear();
        }
    }

    public bool Record(MediaItem item)
    {
        if (string.IsNullOrWhiteSpace(item.SourceUrl))
            return false;
        return RecordUrl(item.SourceUrl, item.Title, item.Kind, item.Duration, item.Format, item.CoverHue);
    }

    public bool RecordUrl(
        string url,
        string? title = null,
        MediaKind kind = MediaKind.Video,
        string duration = "--:--",
        string format = "URL",
        string coverHue = "195")
    {
        if (!MediaEngine.TryNormalizeStreamUri(url, out var uri))
            return false;

        Load();

        var absolute = uri.AbsoluteUri;
        for (var i = Items.Count - 1; i >= 0; i--)
        {
            if (string.Equals(Items[i].SourceUrl, absolute, StringComparison.OrdinalIgnoreCase))
                Items.RemoveAt(i);
        }

        var displayTitle = string.IsNullOrWhiteSpace(title)
            ? (!string.IsNullOrWhiteSpace(Path.GetFileName(uri.AbsolutePath)) && uri.AbsolutePath != "/"
                ? Uri.UnescapeDataString(Path.GetFileName(uri.AbsolutePath))
                : uri.Host)
            : title;

        Items.Insert(0, new RecentPlayEntry
        {
            Title = displayTitle!,
            Subtitle = absolute,
            SourceUrl = absolute,
            Kind = kind is MediaKind.Audio ? MediaKind.Audio : MediaKind.Video,
            Duration = string.IsNullOrWhiteSpace(duration) ? "--:--" : duration,
            Format = string.IsNullOrWhiteSpace(format) ? "URL" : format,
            CoverHue = string.IsNullOrWhiteSpace(coverHue) ? "195" : coverHue,
            PlayedAtUtc = DateTime.UtcNow
        });

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
            var dto = new StreamFileDto
            {
                Entries = Items.Select(e => new StreamEntryDto
                {
                    Title = e.Title,
                    SourceUrl = e.SourceUrl,
                    Kind = e.Kind.ToString(),
                    Duration = e.Duration,
                    Format = e.Format,
                    CoverHue = e.CoverHue,
                    Bitrate = e.Bitrate,
                    PlayedAtUtc = e.PlayedAtUtc
                }).ToList()
            };
            File.WriteAllText(_filePath, JsonSerializer.Serialize(dto, JsonOptions));
        }
        catch
        {
            // Best-effort persistence.
        }
    }

    private sealed class StreamFileDto
    {
        public List<StreamEntryDto> Entries { get; set; } = [];
    }

    private sealed class StreamEntryDto
    {
        public string? Title { get; set; }
        public string? SourceUrl { get; set; }
        public string? Kind { get; set; }
        public string? Duration { get; set; }
        public string? Format { get; set; }
        public string? CoverHue { get; set; }
        public string? Bitrate { get; set; }
        public DateTime PlayedAtUtc { get; set; }
    }
}
