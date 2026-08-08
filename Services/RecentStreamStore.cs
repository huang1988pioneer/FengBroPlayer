using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MusicVideoMediaPlayer.Models;

namespace MusicVideoMediaPlayer.Services;

/// <summary>
/// Persists recently opened network stream URLs (separate from local recent play).
/// Stored under LocalApplicationData as recent-streams.json.
/// Disk writes are debounced so source switching stays responsive.
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
    private readonly object _saveGate = new();
    private CancellationTokenSource? _saveCts;
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

        if (Items.Count > 0 &&
            string.Equals(Items[0].SourceUrl, absolute, StringComparison.OrdinalIgnoreCase))
        {
            Items[0].PlayedAtUtc = DateTime.UtcNow;
            ScheduleSave();
            return true;
        }

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

    public void ScheduleSave()
    {
        List<StreamEntryDto> snapshot;
        lock (_saveGate)
        {
            snapshot = Items.Select(e => new StreamEntryDto
            {
                Title = e.Title,
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
                var dto = new StreamFileDto { Entries = snapshot };
                var json = JsonSerializer.Serialize(dto, JsonOptions);
                await File.WriteAllTextAsync(path, json, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
            }
        }, cts.Token);
    }

    public void Save() => ScheduleSave();

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
