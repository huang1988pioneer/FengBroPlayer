using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using FengBroPlayer.Models;
using FengBroPlayer.ViewModels;

var itemCount = args.Length == 0 ? 200 : int.Parse(args[0]);
var rounds = args.Length < 2 ? 5 : int.Parse(args[1]);
var tempDirectory = Path.Combine(Path.GetTempPath(), $"playlist-delete-probe-{Guid.NewGuid():N}");
Directory.CreateDirectory(tempDirectory);

try
{
    var viewModel = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
    var playlistField = typeof(MainViewModel).GetField("<Playlist>k__BackingField",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Playlist backing field was not found.");
    var playlist = (ObservableCollection<MediaItem>)(Activator.CreateInstance(playlistField.FieldType)
        ?? throw new InvalidOperationException("Playlist collection could not be created."));
    playlistField.SetValue(viewModel, playlist);

    for (var round = 1; round <= rounds; round++)
    {
        for (var index = 0; index < itemCount; index++)
        {
            var path = Path.Combine(tempDirectory, $"track-{round:D2}-{index:D4}.mp3");
            await File.WriteAllBytesAsync(path, []);
            playlist.Add(new MediaItem
            {
                Index = index + 1,
                Kind = MediaKind.Audio,
                Title = $"Track {index + 1}",
                FilePath = path
            });
        }

        var collectionEvents = 0;
        NotifyCollectionChangedEventHandler handler = (_, _) => collectionEvents++;
        playlist.CollectionChanged += handler;
        var candidates = playlist.ToArray();
        var timer = Stopwatch.StartNew();
        await viewModel.DeleteLocalMediaAsync(candidates);
        timer.Stop();
        playlist.CollectionChanged -= handler;

        Console.WriteLine(
            $"round={round} items={itemCount} collection-events={collectionEvents} elapsed-ms={timer.ElapsedMilliseconds}");
        if (playlist.Count != 0)
            throw new InvalidOperationException($"Expected an empty playlist, found {playlist.Count} items.");
        if (collectionEvents > 1)
            throw new InvalidOperationException(
                $"Batch deletion emitted {collectionEvents} collection events; queued UI work grows with every deleted file.");
    }
}
finally
{
    if (Directory.Exists(tempDirectory))
        Directory.Delete(tempDirectory, recursive: true);
}
