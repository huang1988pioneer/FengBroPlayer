using System;
using System.Runtime.InteropServices;
using System.Threading;
using LibVLCSharp.Shared;

class Program
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateWindowEx(int dwExStyle, string lpClassName, string lpWindowName, uint dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);
    [DllImport("user32.dll", SetLastError = true)] static extern bool DestroyWindow(IntPtr hWnd);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string? lpModuleName);
    const uint WsPopup = 0x80000000; const uint WsVisible = 0x10000000;

    static int Main(string[] args)
    {
        var path = args[0];
        Core.Initialize();
        using var libV = new LibVLC("--no-video-title-show", "--quiet");
        using var libA = new LibVLC("--no-video-title-show", "--quiet", "--no-video");
        using var video = new MediaPlayer(libV);
        using var audio = new MediaPlayer(libA);

        var hwnd = CreateWindowEx(0, "Static", "", WsPopup | WsVisible, 0, 0, 320, 180, IntPtr.Zero, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);
        Console.WriteLine("hwnd=" + hwnd);
        video.Hwnd = hwnd;

        // play a bit of "video" as audio file (mp3) on video player with hwnd
        using (var m = new Media(libV, path, FromType.FromPath))
        {
            video.Play(m);
            Thread.Sleep(500);
            Console.WriteLine("video playing mp3 with hwnd state=" + video.State);
        }

        Console.WriteLine("Destroy HWND while player still attached...");
        DestroyWindow(hwnd);
        Console.WriteLine("Destroyed. Calling Stop on UI-like thread...");
        var t0 = Environment.TickCount64;
        try {
            video.Hwnd = IntPtr.Zero;
            video.Stop();
            Console.WriteLine("Stop took " + (Environment.TickCount64 - t0) + "ms state=" + video.State);
        } catch (Exception ex) { Console.WriteLine("Stop EX: " + ex); }

        Console.WriteLine("Now audio play...");
        using var ma = new Media(libA, path, FromType.FromPath);
        audio.Play(ma);
        Thread.Sleep(1000);
        Console.WriteLine("audio state=" + audio.State + " t=" + audio.Time);
        audio.Stop();
        Console.WriteLine("OK");
        return 0;
    }
}
