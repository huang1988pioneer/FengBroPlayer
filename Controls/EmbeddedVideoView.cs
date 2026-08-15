using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using LibVLCSharp.Shared;

namespace MusicVideoMediaPlayer.Controls;

/// <summary>
/// Embeds LibVLC video into a true child HWND (Windows) via NativeControlHost.
/// Avoids the floating / separate-window behavior of some LibVLCSharp.Avalonia builds
/// when MediaPlayer starts before a host handle exists.
/// </summary>
public sealed class EmbeddedVideoView : NativeControlHost
{
    public static readonly StyledProperty<MediaPlayer?> MediaPlayerProperty =
        AvaloniaProperty.Register<EmbeddedVideoView, MediaPlayer?>(nameof(MediaPlayer));

    private IPlatformHandle? _platformHandle;

    public MediaPlayer? MediaPlayer
    {
        get => GetValue(MediaPlayerProperty);
        set => SetValue(MediaPlayerProperty, value);
    }

    /// <summary>True when a native video host handle is available for LibVLC.</summary>
    public bool HasHostHandle =>
        _platformHandle is not null && _platformHandle.Handle != IntPtr.Zero;

    public event EventHandler? HostReady;

    static EmbeddedVideoView()
    {
        MediaPlayerProperty.Changed.AddClassHandler<EmbeddedVideoView>(
            (view, e) => view.OnMediaPlayerChanged(e));
    }

    private void OnMediaPlayerChanged(AvaloniaPropertyChangedEventArgs e)
    {
        if (e.OldValue is MediaPlayer oldPlayer)
        {
            try { oldPlayer.Hwnd = IntPtr.Zero; }
            catch { /* ignore */ }
        }

        AttachPlayerToHost();
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Child static control parented to Avalonia's native handle — true embedding.
            var hwnd = CreateWindowEx(
                dwExStyle: 0,
                lpClassName: "Static",
                lpWindowName: string.Empty,
                dwStyle: WsChild | WsVisible | WsClipSiblings | WsClipChildren,
                x: 0,
                y: 0,
                nWidth: Math.Max(1, (int)Bounds.Width),
                nHeight: Math.Max(1, (int)Bounds.Height),
                hWndParent: parent.Handle,
                hMenu: IntPtr.Zero,
                hInstance: GetModuleHandle(null),
                lpParam: IntPtr.Zero);

            if (hwnd == IntPtr.Zero)
            {
                // Fallback: let Avalonia create a default host if Win32 create failed.
                _platformHandle = base.CreateNativeControlCore(parent);
            }
            else
            {
                _platformHandle = new PlatformHandle(hwnd, "HWND");
            }

            AttachPlayerToHost();
            HostReady?.Invoke(this, EventArgs.Empty);
            return _platformHandle;
        }

        // Non-Windows: use default native host (platform-specific LibVLC integration).
        _platformHandle = base.CreateNativeControlCore(parent);
        AttachPlayerToHost();
        HostReady?.Invoke(this, EventArgs.Empty);
        return _platformHandle;
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        // Never Stop() here. Stop() on the UI thread during layout is a known
        // LibVLC freeze. Window close stops the player first (MainWindow.Closing)
        // so this path should only see a detached or already-stopped player.
        if (MediaPlayer is not null)
        {
            try
            {
                var state = MediaPlayer.State;
                if (state is VLCState.Playing or VLCState.Buffering or VLCState.Opening)
                {
                    try { MediaPlayer.SetPause(true); } catch { /* ignore */ }
                }
            }
            catch { /* ignore */ }

            try { MediaPlayer.Hwnd = IntPtr.Zero; }
            catch { /* ignore */ }
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            && control.Handle != IntPtr.Zero
            && control.HandleDescriptor == "HWND")
        {
            try { DestroyWindow(control.Handle); }
            catch { /* ignore */ }
        }
        else
        {
            base.DestroyNativeControlCore(control);
        }

        _platformHandle = null;
    }

    private void AttachPlayerToHost()
    {
        if (MediaPlayer is null || _platformHandle is null || _platformHandle.Handle == IntPtr.Zero)
            return;

        try
        {
            // Binding HWND before Play keeps video inside this child control.
            MediaPlayer.Hwnd = _platformHandle.Handle;
        }
        catch
        {
            // Ignore attach races during teardown.
        }
    }

    /// <summary>Re-apply HWND (call before starting playback).</summary>
    public void EnsureAttached() => AttachPlayerToHost();

    #region Win32

    private const uint WsChild = 0x40000000;
    private const uint WsVisible = 0x10000000;
    private const uint WsClipSiblings = 0x04000000;
    private const uint WsClipChildren = 0x02000000;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        int dwExStyle,
        string lpClassName,
        string lpWindowName,
        uint dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    #endregion
}
