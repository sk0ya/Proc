using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Proc;

public static class IconHelper
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes,
        ref SHFILEINFO psfi, uint cbSizeFileInfo, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    private const uint SHGFI_ICON = 0x100;
    private const uint SHGFI_SMALLICON = 0x1;

    private static readonly ConcurrentDictionary<string, ImageSource?> _iconCache = new();
    private static readonly Dictionary<string, string> _exePathCache = new();
    private static readonly string _cacheFilePath;
    private static readonly object _lock = new();

    static IconHelper()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Proc");
        Directory.CreateDirectory(dir);
        _cacheFilePath = Path.Combine(dir, "exepaths.txt");
        LoadPathCache();
    }

    private static void LoadPathCache()
    {
        try
        {
            if (!File.Exists(_cacheFilePath)) return;
            foreach (var line in File.ReadAllLines(_cacheFilePath, Encoding.UTF8))
            {
                var idx = line.IndexOf('|');
                if (idx > 0)
                {
                    var name = line[..idx];
                    var path = line[(idx + 1)..];
                    if (File.Exists(path))
                        _exePathCache[name] = path;
                }
            }
        }
        catch { }
    }

    private static void SavePathCache()
    {
        try
        {
            var sb = new StringBuilder();
            lock (_lock)
            {
                foreach (var kv in _exePathCache)
                    sb.AppendLine($"{kv.Key}|{kv.Value}");
            }
            File.WriteAllText(_cacheFilePath, sb.ToString(), Encoding.UTF8);
        }
        catch { }
    }

    /// <summary>
    /// Register a known process name -> exe path mapping. Persists to disk.
    /// </summary>
    public static void RegisterExePath(string processName, string exePath)
    {
        lock (_lock)
        {
            if (_exePathCache.TryGetValue(processName, out var existing) && existing == exePath)
                return;
            _exePathCache[processName] = exePath;
        }
        SavePathCache();
    }

    /// <summary>
    /// Resolve exe path for a process name. Checks cached paths, then running processes.
    /// </summary>
    public static string? ResolveExePath(string processName)
    {
        if (string.IsNullOrEmpty(processName)) return null;

        lock (_lock)
        {
            if (_exePathCache.TryGetValue(processName, out var cached))
                return cached;
        }

        // Try to find from running processes
        try
        {
            var procs = Process.GetProcessesByName(processName);
            foreach (var proc in procs)
            {
                try
                {
                    if (proc.MainModule?.FileName is string path)
                    {
                        RegisterExePath(processName, path);
                        return path;
                    }
                }
                catch { }
                finally { proc.Dispose(); }
            }
        }
        catch { }

        return null;
    }

    public static ImageSource? GetIcon(string? exePath)
    {
        if (string.IsNullOrEmpty(exePath)) return null;
        if (_iconCache.TryGetValue(exePath, out var cached)) return cached;

        ImageSource? result = null;
        var shfi = new SHFILEINFO();
        try
        {
            var hr = SHGetFileInfo(exePath, 0, ref shfi, (uint)Marshal.SizeOf(shfi), SHGFI_ICON | SHGFI_SMALLICON);
            if (hr != IntPtr.Zero && shfi.hIcon != IntPtr.Zero)
            {
                result = Imaging.CreateBitmapSourceFromHIcon(shfi.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                result.Freeze();
            }
        }
        catch { }
        finally
        {
            if (shfi.hIcon != IntPtr.Zero)
                DestroyIcon(shfi.hIcon);
        }

        _iconCache[exePath] = result;
        return result;
    }

    /// <summary>
    /// Get icon by process name. Resolves exe path automatically.
    /// </summary>
    public static ImageSource? GetIconByProcessName(string processName)
    {
        var path = ResolveExePath(processName);
        return GetIcon(path);
    }
}
