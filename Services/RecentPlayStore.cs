using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using FengBroPlayer33.Models;

namespace FengBroPlayer33.Services;

/// <summary>
/// Persists recently played media under LocalApplicationData.
/// Newest entries first; de-duplicated by file path or URL.
/// Disk writes are debounced so track switching stays smooth.
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
    private readonly object _saveGate = new();
    private CancellationTokenSource? _saveCts;
    private bool _loaded;

    public ObservableCollection<RecentPlayEntry> Items { get; } = [];

    public RecentPlayStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FengBroPlayer33");
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

        // Already top — only refresh timestamp without reshuffling the list.
        if (Items.Count > 0 &&
            string.Equals(Items[0].Key, key, StringComparison.OrdinalIgnoreCase))
        {
            Items[0].PlayedAtUtc = DateTime.UtcNow;
            ScheduleSave();
            return true;
        }

        for (var i = Items.Count - 1; i >= 0; i--)
        {
            if (string.Equals(Items[i].Key, key, StringComparison.OrdinalIgnoreCase))
                Items.RemoveAt(i);
        }

        Items.Insert(0, RecentPlayEntry.FromMedia(item, DateTime.UtcNow));

        while (Items.Count > MaxEntries)
            Items.RemoveAt(Items.Count - 1);

        ScheduleSave();
        return true;
    }

    public void Remove(RecentPlayEntry entry)
    {
        Load();
        if (Items.Remove(entry))
            ScheduleSave();
    }

    public void Clear()
    {
        Load();
        if (Items.Count == 0) return;
        Items.Clear();
        ScheduleSave();
    }

    /// <summary>Debounced async write — never blocks the UI during SelectMedia.</summary>
    public void ScheduleSave()
    {
        List<RecentEntryDto> snapshot;
        lock (_saveGate)
        {
            snapshot = Items.Select(e => new RecentEntryDto
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
            }).ToList();
        }

        _saveCts?.Cancel();
        _saveCts?.Dispose();
        var cts = new CancellationTokenSource();
        _saveCts = cts;
        var path = _filePath;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(350, cts.Token).ConfigureAwait(false);
                var dto = new RecentFileDto { Entries = snapshot };
                var json = JsonSerializer.Serialize(dto, JsonOptions);
                await File.WriteAllTextAsync(path, json, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Newer save scheduled.
            }
            catch
            {
                // Disk full / permissions — ignore.
            }
        }, cts.Token);
    }

    public void Save() => ScheduleSave();

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
