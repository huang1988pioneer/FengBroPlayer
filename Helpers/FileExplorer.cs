using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace FengBroPlayer.Helpers;

/// <summary>
/// Opens the system file manager and highlights a local file.
/// </summary>
public static class FileExplorer
{
    public static void Reveal(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);

        if (OperatingSystem.IsWindows())
        {
            RevealOnWindows(fullPath);
            return;
        }

        if (OperatingSystem.IsMacOS())
        {
            var startInfo = new ProcessStartInfo { FileName = "open", UseShellExecute = false };
            startInfo.ArgumentList.Add("-R");
            startInfo.ArgumentList.Add(fullPath);
            Process.Start(startInfo);
            return;
        }

        var folder = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(folder))
            throw new InvalidOperationException("找不到檔案所在資料夾");

        var linux = new ProcessStartInfo { FileName = "xdg-open", UseShellExecute = false };
        linux.ArgumentList.Add(folder);
        Process.Start(linux);
    }

    [SupportedOSPlatform("windows")]
    private static void RevealOnWindows(string fullPath)
    {
        if (TrySelectWithShellApi(fullPath))
            return;

        // explorer /select,<path> — comma is part of the switch; path is a
        // separate argument so spaces and quotes parse correctly.
        var startInfo = new ProcessStartInfo
        {
            FileName = "explorer.exe",
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("/select,");
        startInfo.ArgumentList.Add(fullPath);

        using var process = Process.Start(startInfo);
        if (process is null)
            throw new InvalidOperationException("無法啟動檔案總管");
    }

    [SupportedOSPlatform("windows")]
    private static bool TrySelectWithShellApi(string fullPath)
    {
        var folder = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(folder))
            return false;

        var hr = SHParseDisplayName(fullPath, IntPtr.Zero, out var filePidl, 0, out _);
        if (hr != 0 || filePidl == IntPtr.Zero)
            return false;

        try
        {
            hr = SHParseDisplayName(folder, IntPtr.Zero, out var folderPidl, 0, out _);
            if (hr != 0 || folderPidl == IntPtr.Zero)
                return false;

            try
            {
                IntPtr[] items = [ILFindLastID(filePidl)];
                if (items[0] == IntPtr.Zero)
                    return false;
                return SHOpenFolderAndSelectItems(folderPidl, 1, items, 0) == 0;
            }
            finally
            {
                Marshal.FreeCoTaskMem(folderPidl);
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(filePidl);
        }
    }

    [SupportedOSPlatform("windows")]
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(
        string name,
        IntPtr bindingContext,
        out IntPtr pidl,
        uint sfgaoIn,
        out uint psfgaoOut);

    [SupportedOSPlatform("windows")]
    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern int SHOpenFolderAndSelectItems(
        IntPtr pidlFolder,
        uint cidl,
        [In, MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl,
        uint dwFlags);

    [SupportedOSPlatform("windows")]
    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern IntPtr ILFindLastID(IntPtr pidl);
}
