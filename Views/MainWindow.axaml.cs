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

    private static readonly FilePickerFileType SubtitleType = new("字幕檔案")
    {
        Patterns = MediaMetadata.SubtitleExtensions.Select(e => $"*{e}").ToArray()
    };

    private static readonly FilePickerFileType LyricsType = new("LRC Lyrics")
    {
        Patterns = ["*.lrc"],
        MimeTypes = ["text/plain"]
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
        // Handle Tab before Avalonia's normal focus navigation. The XAML bubble
        // handler was too late, so Tab still moved focus like a standard window.
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);

        // Slider marks pointer events Handled on the thumb/track; XAML handlers miss them
        // unless we subscribe with handledEventsToo. Without BeginSeek, Progress snaps back
        // on TimeChanged and LibVLC never receives the seek (mp3/mp4).
        // Press: tunnel so _isSeeking is set before Value moves.
        // Release: bubble so Track/Thumb has already committed the final Value (tunnel
        // EndSeek was seeking the pre-click position — looked like "timeline won't jump").
        SeekSlider.AddHandler(PointerPressedEvent, OnSeekStart, RoutingStrategies.Tunnel, handledEventsToo: true);
        SeekSlider.AddHandler(PointerReleasedEvent, OnSeekEnd, RoutingStrategies.Bubble, handledEventsToo: true);
        SeekSlider.AddHandler(PointerCaptureLostEvent, OnSeekCaptureLost, RoutingStrategies.Bubble, handledEventsToo: true);

        _fsHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _fsHideTimer.Tick += OnFullscreenHideTick;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        vm.PickFilesAsync = PickFilesAsync;
        vm.PickFolderAsync = PickFolderAsync;
        vm.PrepareVideoHost = PrepareVideoHost;
        vm.RequestFullscreen = EnterFullscreen;
        vm.ExitFullscreen = LeaveFullscreen;
        vm.RequestClose = Close;
        vm.SetTopmost = top => Topmost = top;
        vm.PromptNetworkUrlAsync = PromptNetworkUrlAsync;
        vm.PickSubtitleAsync = PickSubtitleFileAsync;
        vm.PickLyricsAsync = PickLyricsFileAsync;
        vm.PropertyChanged += OnViewModelPropertyChanged;

        // Keep video HWND mounted for the whole session (overlay UI for audio/idle).
        Dispatcher.UIThread.Post(PrepareVideoHost, DispatcherPriority.Loaded);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not MainViewModel vm) return;

        // Video HWND stays mounted; only re-attach when entering video mode.
        if (e.PropertyName == nameof(MainViewModel.ActiveMediaKind)
            && vm.ActiveMediaKind == MediaKind.Video)
        {
            Dispatcher.UIThread.Post(PrepareVideoHost, DispatcherPriority.Loaded);
        }

        if (e.PropertyName == nameof(MainViewModel.IsControlBarVisible)
            && vm.CurrentChrome == ChromeMode.Fullscreen
            && vm.IsControlBarVisible)
        {
            RestartFullscreenHideTimer();
        }
    }

    private void PrepareVideoHost()
    {
        if (DataContext is not MainViewModel vm)
            return;

        if (VideoHost.MediaPlayer is null)
            VideoHost.MediaPlayer = vm.VideoMediaPlayer;
        else
            VideoHost.EnsureAttached();

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

    private async Task<string?> PickSubtitleFileAsync()
    {
        var options = new FilePickerOpenOptions
        {
            AllowMultiple = false,
            Title = "開啟字幕檔案",
            FileTypeFilter = [SubtitleType, FilePickerFileTypes.All]
        };
        var files = await StorageProvider.OpenFilePickerAsync(options);
        if (files.Count == 0) return null;
        return files[0].TryGetLocalPath();
    }

    private async Task<string?> PickFolderAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            AllowMultiple = false,
            Title = "選擇媒體資料夾"
        });
        return folders.Count == 0 ? null : folders[0].TryGetLocalPath();
    }

    private async Task<string?> PickLyricsFileAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            Title = "選擇 LRC 動態歌詞",
            FileTypeFilter = [LyricsType, FilePickerFileTypes.All]
        });
        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }

    private async Task<string?> PromptNetworkUrlAsync()
    {
        var vm = DataContext as MainViewModel;
        var box = new TextBox
        {
            PlaceholderText = "https://example.com/stream.m3u8 或音訊/影片直連網址",
            MinWidth = 400,
            Text = vm?.NetworkUrl ?? "",
            Classes = { "search" }
        };

        var hasHistory = vm is { HasRecentStreamItems: true };
        var dialog = new Window
        {
            Title = "開啟網路串流",
            Width = 520,
            Height = hasHistory ? 420 : 190,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = true,
            MinWidth = 420,
            MinHeight = hasHistory ? 320 : 180,
            Background = Background
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
            var b = new Button
            {
                Content = text,
                MinWidth = 80,
                IsDefault = isDefault,
                IsCancel = !isDefault,
                Classes = { isDefault ? "chip" : "ghost" }
            };
            b.Click += (_, _) =>
            {
                if (isDefault) Accept();
                else Cancel();
            };
            return b;
        }

        var root = new StackPanel
        {
            Margin = new Avalonia.Thickness(16),
            Spacing = 12
        };

        root.Children.Add(new TextBlock
        {
            Text = "輸入可直接播放的串流網址（http/https/rtsp，可省略 https://）：",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });
        root.Children.Add(box);

        if (vm is not null && vm.RecentStreamItems.Count > 0)
        {
            root.Children.Add(new TextBlock
            {
                Text = "最近網路串流（點一下填入並播放）：",
                Classes = { "muted" },
                FontSize = 12
            });

            var historyPanel = new StackPanel { Spacing = 2 };
            foreach (var entry in vm.RecentStreamItems.Take(12))
            {
                var url = entry.SourceUrl;
                if (string.IsNullOrWhiteSpace(url)) continue;

                var row = new Button
                {
                    Classes = { "playlist-row" },
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                    Padding = new Avalonia.Thickness(10, 8),
                    Content = new StackPanel
                    {
                        Spacing = 2,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = entry.Title,
                                FontSize = 13,
                                TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis
                            },
                            new TextBlock
                            {
                                Text = url,
                                Classes = { "muted" },
                                FontSize = 11,
                                TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis
                            }
                        }
                    }
                };
                row.Click += (_, _) =>
                {
                    box.Text = url;
                    result = url;
                    dialog.Close();
                };
                historyPanel.Children.Add(row);
            }

            root.Children.Add(new ScrollViewer
            {
                MaxHeight = 200,
                Content = historyPanel,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
            });
        }

        root.Children.Add(new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Spacing = 8,
            Children =
            {
                CreateDialogButton("取消", false),
                CreateDialogButton("播放", true)
            }
        });

        dialog.Content = root;

        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Accept();
                e.Handled = true;
            }
        };

        dialog.Opened += (_, _) =>
        {
            box.Focus();
            box.CaretIndex = box.Text?.Length ?? 0;
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
            case Key.Delete:
                // Only let Delete act on a playlist row. This prevents accidental
                // file deletion while the video surface or another control is active.
                if (PlaylistList.IsKeyboardFocusWithin)
                {
                    var selectedItems = PlaylistList.SelectedItems?
                        .OfType<MediaItem>()
                        .Where(item => item.IsLocalFile && !string.IsNullOrWhiteSpace(item.FilePath))
                        .ToList() ?? [];
                    if (selectedItems.Count == 0 && vm.CurrentMedia is { IsLocalFile: true } current)
                        selectedItems.Add(current);

                    _ = ConfirmDeleteMediaAsync(vm, selectedItems);
                    e.Handled = true;
                }
                break;
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
            case Key.Tab:
                vm.ToggleMediaInfoCommand.Execute(null);
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

    private async Task ConfirmDeleteMediaAsync(MainViewModel vm, IReadOnlyList<MediaItem> items)
    {
        if (items.Count == 0)
            return;

        var isBatch = items.Count > 1;
        var title = isBatch ? $"確認刪除 {items.Count} 個檔案" : "確認刪除檔案";
        var detail = isBatch
            ? string.Join(Environment.NewLine, items.Take(3).Select(item => item.Title))
                + (items.Count > 3 ? $"{Environment.NewLine}…以及其他 {items.Count - 3} 個檔案" : "")
            : items[0].FilePath;

        var dialog = new Window
        {
            Title = title,
            Width = 500,
            Height = isBatch ? 250 : 210,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Background
        };

        var confirmed = false;
        void Close(bool shouldDelete)
        {
            confirmed = shouldDelete;
            dialog.Close();
        }

        var deleteButton = new Button
        {
            Content = "刪除",
            MinWidth = 84,
            IsDefault = true,
            Classes = { "chip" }
        };
        deleteButton.Click += (_, _) => Close(true);

        var cancelButton = new Button
        {
            Content = "取消",
            MinWidth = 84,
            IsCancel = true,
            Classes = { "ghost" }
        };
        cancelButton.Click += (_, _) => Close(false);

        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(24),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = isBatch ? $"要刪除選取的 {items.Count} 個檔案嗎？" : "要刪除此檔案嗎？", FontSize = 18, FontWeight = Avalonia.Media.FontWeight.SemiBold },
                new TextBlock { Text = isBatch ? "此操作會永久刪除下列檔案：" : items[0].Title, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis },
                new TextBlock
                {
                    Text = detail,
                    Classes = { "muted" },
                    FontSize = 11,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    MaxHeight = 64,
                    TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis
                },
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancelButton, deleteButton }
                }
            }
        };

        await dialog.ShowDialog(this);
        if (confirmed)
            vm.DeleteLocalMedia(items);
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
        if (sender is not Control c)
            return;
        if (!e.GetCurrentPoint(c).Properties.IsLeftButtonPressed)
            return;
        if (DataContext is MainViewModel vm)
            vm.BeginSeek();
    }

    private void OnSeekEnd(object? sender, PointerReleasedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;
        // Prefer the Slider's committed value over binding lag.
        if (sender is Slider slider)
            vm.Progress = slider.Value;
        vm.EndSeek();
    }

    private void OnSeekCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;
        vm.Progress = SeekSlider.Value;
        vm.EndSeek();
    }

    private void OnSeekKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Left or Key.Right or Key.Home or Key.End or Key.Up or Key.Down)
        {
            if (DataContext is MainViewModel vm)
            {
                if (sender is Slider slider)
                    vm.Progress = slider.Value;
                vm.BeginSeek();
                vm.EndSeek();
            }
        }
    }
}
