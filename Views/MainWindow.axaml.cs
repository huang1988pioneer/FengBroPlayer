using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MusicVideoMediaPlayer.Services;
using MusicVideoMediaPlayer.ViewModels;

namespace MusicVideoMediaPlayer.Views;

public partial class MainWindow : Window
{
    private static readonly FilePickerFileType AudioType = new("音樂檔案")
    {
        Patterns = MediaMetadata.AudioExtensions.Select(e => $"*{e}").ToArray(),
        MimeTypes = ["audio/*"]
    };

    private static readonly FilePickerFileType VideoType = new("影片檔案")
    {
        Patterns = MediaMetadata.VideoExtensions.Select(e => $"*{e}").ToArray(),
        MimeTypes = ["video/*"]
    };

    private static readonly FilePickerFileType MediaType = new("媒體檔案")
    {
        Patterns = MediaMetadata.AudioExtensions
            .Concat(MediaMetadata.VideoExtensions)
            .Select(e => $"*{e}")
            .ToArray()
    };

    public MainWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
        Closed += OnClosed;
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        vm.PickFilesAsync = PickFilesAsync;
        vm.PrepareVideoHost = PrepareVideoHost;

        // Attach player after first layout so the child HWND exists.
        Dispatcher.UIThread.Post(PrepareVideoHost, DispatcherPriority.Loaded);
    }

    private void PrepareVideoHost()
    {
        if (DataContext is not MainViewModel vm)
            return;

        // Keep MediaPlayer bound and HWND re-applied before every Play.
        if (VideoHost.MediaPlayer is null)
            VideoHost.MediaPlayer = vm.VideoMediaPlayer;

        VideoHost.EnsureAttached();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.Dispose();
    }

    private async Task<IReadOnlyList<string>> PickFilesAsync(string kind)
    {
        var options = new FilePickerOpenOptions
        {
            AllowMultiple = true,
            Title = kind switch
            {
                "audio" => "開啟音樂檔案",
                "video" => "開啟影片檔案",
                _ => "開啟媒體檔案"
            },
            FileTypeFilter = kind switch
            {
                "audio" => [AudioType, FilePickerFileTypes.All],
                "video" => [VideoType, FilePickerFileTypes.All],
                _ => [MediaType, AudioType, VideoType, FilePickerFileTypes.All]
            }
        };

        var files = await StorageProvider.OpenFilePickerAsync(options);
        if (files.Count == 0)
            return Array.Empty<string>();

        var paths = new List<string>(files.Count);
        foreach (var file in files)
        {
            var path = file.TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(path))
                paths.Add(path);
        }

        return paths;
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Formats.Contains(DataFormat.File)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        if (!e.DataTransfer.Formats.Contains(DataFormat.File))
            return;

        var items = e.DataTransfer.TryGetFiles();
        if (items is null) return;

        var paths = new List<string>();
        foreach (var item in items)
        {
            if (item is IStorageFile file)
            {
                var path = file.TryGetLocalPath();
                if (!string.IsNullOrWhiteSpace(path))
                    paths.Add(path);
            }
            else
            {
                var path = item.TryGetLocalPath();
                if (!string.IsNullOrWhiteSpace(path) && System.IO.File.Exists(path))
                    paths.Add(path);
            }
        }

        if (paths.Count > 0)
            vm.ImportDroppedPaths(paths);
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        if (e.Key == Key.O && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            vm.OpenMediaCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Space)
        {
            if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox)
                return;
            vm.ToggleMusicPlayCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnVideoStagePressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.ToggleVideoPlayCommand.Execute(null);
        e.Handled = true;
    }

    private void OnMusicSeekStart(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.BeginMusicSeek();
    }

    private void OnMusicSeekEnd(object? sender, PointerReleasedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.EndMusicSeek();
    }

    private void OnVideoSeekStart(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.BeginVideoSeek();
    }

    private void OnVideoSeekEnd(object? sender, PointerReleasedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.EndVideoSeek();
    }

    private void OnVideoSeekCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        // Mouse released outside the slider — still commit scrub position.
        if (DataContext is MainViewModel vm)
            vm.EndVideoSeek();
    }

    private void OnVideoSeekKeyUp(object? sender, KeyEventArgs e)
    {
        // Arrow keys on focused slider.
        if (e.Key is Key.Left or Key.Right or Key.Home or Key.End or Key.Up or Key.Down)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.BeginVideoSeek();
                vm.EndVideoSeek();
            }
        }
    }
}
