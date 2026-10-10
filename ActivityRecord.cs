using System.Globalization;

namespace Proc;

public record ActivityRecord(DateTime Timestamp, string ProcessName, string WindowTitle, bool IsIdle = false)
{
    private static readonly string[] TimeFormats = ["HH:mm:ss", "H:mm:ss"];

    public string ToCsvLine() => IsIdle
        ? $"{Timestamp:HH:mm:ss},{CsvUtil.Escape(ProcessName)},{CsvUtil.Escape(WindowTitle)},idle"
        : $"{Timestamp:HH:mm:ss},{CsvUtil.Escape(ProcessName)},{CsvUtil.Escape(WindowTitle)}";

    public static ActivityRecord FromCsvLine(string line)
    {
        var parts = CsvUtil.ParseLine(line);
        if (parts.Count < 3) throw new FormatException("Invalid CSV line");
        bool isIdle = parts.Count >= 4 && parts[3] == "idle";
        return new ActivityRecord(
            DateTime.ParseExact(parts[0], TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None),
            parts[1],
            parts[2],
            isIdle);
    }
}
