using Avalonia;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace MusicVideoMediaPlayer;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // On Apple Silicon, VideoLAN.LibVLC.Mac is x86_64-only. Prefer system VLC.app,
        // but DYLD_LIBRARY_PATH must be set before process start (in-process setenv is ignored).
        if (TryRelaunchWithSystemVlc(args))
            return;

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    /// <returns>true if this process should exit (child took over).</returns>
    private static bool TryRelaunchWithSystemVlc(string[] args)
    {
        if (!OperatingSystem.IsMacOS())
            return false;
        if (RuntimeInformation.OSArchitecture != Architecture.Arm64)
            return false;
        if (Environment.GetEnvironmentVariable("MUSIC_VIDEO_VLC_READY") == "1")
            return false;

        const string lib = "/Applications/VLC.app/Contents/MacOS/lib";
        const string plugins = "/Applications/VLC.app/Contents/MacOS/plugins";
        if (!Directory.Exists(lib) || !Directory.Exists(plugins))
            return false;

        // Already usable (e.g. user exported env before launch).
        var dyld = Environment.GetEnvironmentVariable("DYLD_LIBRARY_PATH") ?? "";
        if (dyld.Contains(lib, StringComparison.Ordinal))
        {
            Environment.SetEnvironmentVariable("VLC_PLUGIN_PATH", plugins);
            Environment.SetEnvironmentVariable("MUSIC_VIDEO_VLC_READY", "1");
            return false;
        }

        try
        {
            var processPath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(processPath))
                return false;

            var psi = new ProcessStartInfo
            {
                FileName = processPath,
                UseShellExecute = false,
            };

            // `dotnet App.dll`: ProcessPath is the host and GetCommandLineArgs()[0] is the dll
            // (user args are not included there). Forward the entry assembly + Main args.
            // AppHost (`./App`): ProcessPath is the native host; only forward Main args.
            var hostName = Path.GetFileNameWithoutExtension(processPath);
            var isDotnetHost = hostName.Equals("dotnet", StringComparison.OrdinalIgnoreCase);
            if (isDotnetHost)
            {
                var entry = System.Reflection.Assembly.GetEntryAssembly()?.Location
                            ?? (Environment.GetCommandLineArgs().Length > 0
                                ? Environment.GetCommandLineArgs()[0]
                                : null);
                if (string.IsNullOrEmpty(entry) || !File.Exists(entry))
                    return false;
                psi.ArgumentList.Add(entry);
            }

            foreach (var a in args)
                psi.ArgumentList.Add(a);

            psi.Environment["MUSIC_VIDEO_VLC_READY"] = "1";
            psi.Environment["VLC_PLUGIN_PATH"] = plugins;
            psi.Environment["DYLD_LIBRARY_PATH"] = string.IsNullOrEmpty(dyld)
                ? lib
                : lib + Path.PathSeparator + dyld;

            using var child = Process.Start(psi);
            if (child is null)
                return false;
            child.WaitForExit();
            Environment.Exit(child.ExitCode);
            return true;
        }
        catch
        {
            // Fall through and try without re-launch.
            return false;
        }
    }
}
