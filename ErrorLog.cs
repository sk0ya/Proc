using System.IO;

namespace Proc;

/// <summary>
/// Minimal diagnostic log at %LOCALAPPDATA%\Proc\error.log. Repeated identical errors
/// (e.g. from the per-second sampler) are written at most once per 10 minutes.
/// </summary>
public static class ErrorLog
{
    private const long MaxSize = 1024 * 1024;
    private static readonly TimeSpan RepeatInterval = TimeSpan.FromMinutes(10);

    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Proc", "error.log");

    private static readonly object _lock = new();
    private static readonly Dictionary<string, (string Message, DateTime At)> _lastWritten = new();

    public static void Write(string context, Exception ex)
    {
        try
        {
            lock (_lock)
            {
                var now = DateTime.Now;
                var message = $"{ex.GetType().Name}: {ex.Message}";
                if (_lastWritten.TryGetValue(context, out var last)
                    && last.Message == message && now - last.At < RepeatInterval)
                    return;
                _lastWritten[context] = (message, now);

                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                var info = new FileInfo(LogPath);
                if (info.Exists && info.Length > MaxSize)
                    File.Move(LogPath, Path.ChangeExtension(LogPath, ".old.log"), true);

                File.AppendAllText(LogPath, $"{now:yyyy-MM-dd HH:mm:ss} [{context}] {ex}{Environment.NewLine}");
            }
        }
        catch
        {
            // logging must never throw
        }
    }
}
