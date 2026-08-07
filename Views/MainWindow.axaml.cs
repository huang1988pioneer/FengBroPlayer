using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MusicVideoMediaPlayer.Models;
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

    private readonly DispatcherTimer _fsHideTimer;
    private WindowState _stateBeforeFullscreen = WindowState.Normal;
    private bool _applyingWindowState;

    public MainWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
        Closed += OnClosed;
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);

        _fsHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _fsHideTimer.Tick += OnFullscreenHideTick;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        vm.PickFilesAsync = PickFilesAsync;
        vm.PrepareVideoHost = PrepareVideoHost;
        vm.RequestFullscreen = EnterFullscreen;
        vm.ExitFullscreen = LeaveFullscreen;
        vm.RequestClose = Close;
        vm.SetTopmost = top => Topmost = top;
        vm.PromptNetworkUrlAsync = PromptNetworkUrlAsync;
        vm.PropertyChanged += OnViewModelPropertyChanged;

        ApplyStageRowHeights(vm.ActiveMediaKind);
        Dispatcher.UIThread.Post(PrepareVideoHost, DispatcherPriority.Loaded);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not MainViewModel vm) return;

        if (e.PropertyName is nameof(MainViewModel.ActiveMediaKind)
            or nameof(MainViewModel.IsVideoStage)
            or nameof(MainViewModel.IsAudioStage))
        {
            ApplyStageRowHeights(vm.ActiveMediaKind);
            if (vm.ActiveMediaKind == MediaKind.Video)
                Dispatcher.UIThread.Post(PrepareVideoHost, DispatcherPriority.Loaded);
        }

        if (e.PropertyName == nameof(MainViewModel.IsControlBarVisible)
            && vm.CurrentChrome == ChromeMode.Fullscreen
            && vm.IsControlBarVisible)
        {
            RestartFullscreenHideTimer();
        }
    }

    /// <summary>
    /// KD-12 matrix: Video → host *, audio 0; Audio/None → host 0, audio *.
    /// </summary>
    private void ApplyStageRowHeights(MediaKind kind)
    {
        if (StageRoot.RowDefinitions.Count < 2)
            return;

        if (kind == MediaKind.Video)
        {
            StageRoot.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star);
            StageRoot.RowDefinitions[1].Height = new GridLength(0);
        }
        else
        {
            StageRoot.RowDefinitions[0].Height = new GridLength(0);
            StageRoot.RowDefinitions[1].Height = new GridLength(1, GridUnitType.Star);
        }
    }

    private void PrepareVideoHost()
    {
        if (DataContext is not MainViewModel vm)
            return;

        if (VideoHost.MediaPlayer is null)
            VideoHost.MediaPlayer = vm.VideoMediaPlayer;

        VideoHost.EnsureAttached();
    }

    private void EnterFullscreen()
    {
        if (_applyingWindowState) return;
        _applyingWindowState = true;
        try
        {
            if (WindowState != WindowState.FullScreen)
                _stateBeforeFullscreen = WindowState;
            WindowState = WindowState.FullScreen;
            RestartFullscreenHideTimer();
        }
        finally
        {
            _applyingWindowState = false;
        }
    }

    private void LeaveFullscreen()
    {
        if (_applyingWindowState) return;
        _applyingWindowState = true;
        try
        {
            _fsHideTimer.Stop();
            WindowState = _stateBeforeFullscreen == WindowState.FullScreen
                ? WindowState.Normal
                : _stateBeforeFullscreen;
            if (DataContext is MainViewModel vm)
                vm.IsControlBarVisible = true;
        }
        finally
        {
            _applyingWindowState = false;
        }
    }

    private void OnFullscreenHideTick(object? sender, EventArgs e)
    {
        _fsHideTimer.Stop();
        if (DataContext is MainViewModel vm && vm.CurrentChrome == ChromeMode.Fullscreen)
            vm.IsControlBarVisible = false;
    }

    private void RestartFullscreenHideTimer()
    {
        if (DataContext is not MainViewModel vm || vm.CurrentChrome != ChromeMode.Fullscreen)
            return;
        _fsHideTimer.Stop();
        _fsHideTimer.Start();
    }

    private void OnWindowPointerMoved(object? sender, PointerEventArgs e)
    {
        if (DataContext is not MainViewModel vm || vm.CurrentChrome != ChromeMode.Fullscreen)
            return;

        if (!vm.IsControlBarVisible)
            vm.IsControlBarVisible = true;
        RestartFullscreenHideTimer();
    }

    private void OnHotZoneEntered(object? sender, PointerEventArgs e)
    {
        if (DataContext is not MainViewModel vm || vm.CurrentChrome != ChromeMode.Fullscreen)
            return;
        vm.IsControlBarVisible = true;
        RestartFullscreenHideTimer();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _fsHideTimer.Stop();
        if (DataContext is MainViewModel vm)
        {
            vm.PropertyChanged -= OnViewModelPropertyChanged;
            vm.Dispose();
        }
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

    private async Task<string?> PromptNetworkUrlAsync()
    {
        var box = new TextBox
        {
            PlaceholderText = "https://…",
            MinWidth = 360,
            Text = (DataContext as MainViewModel)?.NetworkUrl ?? ""
        };

        var dialog = new Window
        {
            Title = "開啟網路串流",
            Width = 440,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };

        string? result = null;
        void Accept()
        {
            result = box.Text?.Trim();
            dialog.Close();
        }

        void Cancel()
        {
            result = null;
            dialog.Close();
        }

        Button CreateDialogButton(string text, bool isDefault)
        {
            var b = new Button { Content = text, MinWidth = 72, IsDefault = isDefault, IsCancel = !isDefault };
            b.Click += (_, _) =>
            {
                if (isDefault) Accept();
                else Cancel();
            };
            return b;
        }

        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(16),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = "輸入 http(s) 媒體網址：" },
                box,
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Spacing = 8,
                    Children =
                    {
                        CreateDialogButton("取消", false),
                        CreateDialogButton("播放", true)
                    }
                }
            }
        };

        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Accept();
                e.Handled = true;
            }
        };

        await dialog.ShowDialog(this);
        return string.IsNullOrWhiteSpace(result) ? null : result;
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

        // Don't steal keys from text inputs
        if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox)
            return;

        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);

        if (ctrl && e.Key == Key.O)
        {
            vm.OpenMediaCommand.Execute(null);
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.Space:
                vm.TogglePlayCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.S:
                vm.StopMediaCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.M:
                vm.ToggleMuteCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.P:
                vm.TogglePlaylistCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.F:
                vm.ToggleFullscreenCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Escape:
                if (vm.CurrentChrome == ChromeMode.Fullscreen)
                {
                    vm.ToggleFullscreenCommand.Execute(null);
                    e.Handled = true;
                }
                break;
            case Key.Left:
                vm.SeekRelativeCommand.Execute(shift ? -30 : -5);
                e.Handled = true;
                break;
            case Key.Right:
                vm.SeekRelativeCommand.Execute(shift ? 30 : 5);
                e.Handled = true;
                break;
            case Key.Up:
                vm.Volume = Math.Clamp(vm.Volume + 0.05, 0, 1);
                e.Handled = true;
                break;
            case Key.Down:
                vm.Volume = Math.Clamp(vm.Volume - 0.05, 0, 1);
                e.Handled = true;
                break;
        }
    }

    private void OnAudioStagePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        // Only left click toggles play; right-click is context menu
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            vm.TogglePlayCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnAudioStageWheel(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var delta = e.Delta.Y > 0 ? 0.05 : -0.05;
        vm.Volume = Math.Clamp(vm.Volume + delta, 0, 1);
        e.Handled = true;
    }

    private void OnAudioStageDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.ToggleFullscreenCommand.Execute(null);
        e.Handled = true;
    }

    private void OnSeekStart(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.BeginSeek();
    }

    private void OnSeekEnd(object? sender, PointerReleasedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.EndSeek();
    }

    private void OnSeekCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.EndSeek();
    }

    private void OnSeekKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Left or Key.Right or Key.Home or Key.End or Key.Up or Key.Down)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.BeginSeek();
                vm.EndSeek();
            }
        }
    }
}
