using System.Globalization;
using System.IO;
using System.Text;

namespace Proc;

public static class ImportHelper
{
    /// <summary>
    /// Imports records from an external quoted CSV and merges into Proc's daily log files.
    /// Expected columns: Name, Start, End, Duration, Process
    /// Existing Proc records take priority: an imported minute is skipped when the day's
    /// log already has a record in that minute (Proc's own records carry seconds).
    /// </summary>
    public static int Import(string importFilePath, string logDir)
    {
        var lines = File.ReadAllLines(importFilePath, Encoding.UTF8);
        if (lines.Length < 2) return 0;

        // Parse imported records, expanding duration into per-minute entries
        var importedByDate = new Dictionary<DateTime, List<ActivityRecord>>();
        for (int i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;

            var parts = CsvUtil.ParseLine(line);
            if (parts.Count < 5) continue;

            var windowTitle = parts[0].Trim();
            if (!TryParseDateTime(parts[1].Trim(), out var start)) continue;
            if (!TryParseDateTime(parts[2].Trim(), out var end)) continue;
            var processName = parts[4].Trim();

            if (string.IsNullOrEmpty(processName)) continue;

            // Generate one record per minute from start to end
            var current = new DateTime(start.Year, start.Month, start.Day, start.Hour, start.Minute, 0);
            var endMinute = new DateTime(end.Year, end.Month, end.Day, end.Hour, end.Minute, 0);

            do
            {
                var date = current.Date;
                if (!importedByDate.ContainsKey(date))
                    importedByDate[date] = [];

                importedByDate[date].Add(new ActivityRecord(current, processName, windowTitle));
                current = current.AddMinutes(1);
            } while (current <= endMinute);
        }

        // Merge into existing log files (existing records take priority)
        int totalImported = 0;
        foreach (var (date, importedRecords) in importedByDate)
        {
            var filePath = Path.Combine(logDir, $"{date:yyyy-MM-dd}.csv");

            // Load existing records and collect the minutes they cover
            var existingMinutes = new HashSet<string>();
            var existingLines = new List<string>();
            if (File.Exists(filePath))
            {
                foreach (var eline in File.ReadAllLines(filePath, Encoding.UTF8))
                {
                    if (string.IsNullOrWhiteSpace(eline)) continue;
                    existingLines.Add(eline);
                    if (TryGetMinuteKey(eline, out var minuteKey))
                        existingMinutes.Add(minuteKey);
                }
            }

            // Append only non-duplicate imported records
            int added = 0;
            foreach (var record in importedRecords)
            {
                var minuteKey = record.Timestamp.ToString("HH:mm", CultureInfo.InvariantCulture);
                if (existingMinutes.Add(minuteKey))
                {
                    existingLines.Add(record.ToCsvLine());
                    added++;
                }
            }

            if (added > 0)
            {
                Directory.CreateDirectory(logDir);
                File.WriteAllLines(filePath, existingLines, Encoding.UTF8);
                totalImported += added;
            }
        }

        return totalImported;
    }

    /// <summary>Gets "HH:mm" from a log line's leading "HH:mm:ss" timestamp.</summary>
    private static bool TryGetMinuteKey(string logLine, out string minuteKey)
    {
        minuteKey = "";
        var comma = logLine.IndexOf(',');
        if (comma <= 0) return false;
        if (!TimeSpan.TryParseExact(logLine[..comma], [@"hh\:mm\:ss", @"h\:mm\:ss"], CultureInfo.InvariantCulture, out var time))
            return false;
        minuteKey = $"{time.Hours:D2}:{time.Minutes:D2}";
        return true;
    }

    private static bool TryParseDateTime(string s, out DateTime result)
    {
        string[] formats = [
            "yyyy/MM/dd HH:mm:ss",
            "yyyy/MM/dd H:mm:ss",
            "yyyy/MM/dd HH:mm",
            "yyyy/MM/dd H:mm",
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-dd H:mm:ss",
            "yyyy-MM-dd HH:mm",
            "yyyy-MM-dd H:mm",
            "yyyy/M/d HH:mm:ss",
            "yyyy/M/d H:mm:ss",
            "yyyy/M/d HH:mm",
            "yyyy/M/d H:mm",
        ];
        return DateTime.TryParseExact(s, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out result);
    }
}
