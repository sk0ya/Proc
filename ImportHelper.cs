using System.Globalization;
using System.IO;
using System.Text;

namespace Proc;

public static class ImportHelper
{
    /// <summary>
    /// Imports records from an external CSV (tab-separated) and merges into Proc's daily log files.
    /// Existing Proc records take priority on duplicate timestamps.
    /// </summary>
    public static int Import(string importFilePath, string logDir)
    {
        var lines = File.ReadAllLines(importFilePath, Encoding.UTF8);
        if (lines.Length < 2) return 0;

        // Detect delimiter from header
        var header = lines[0];
        var delimiter = header.Contains('\t') ? '\t' : ',';

        // Parse imported records, expanding duration into per-minute entries
        var importedByDate = new Dictionary<DateTime, List<ActivityRecord>>();
        for (int i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;

            var parts = line.Split(delimiter);
            if (parts.Length < 5) continue;

            var windowTitle = parts[0].Trim();
            if (!TryParseDateTime(parts[1].Trim(), out var start)) continue;
            if (!TryParseDateTime(parts[2].Trim(), out var end)) continue;
            var processName = parts[4].Trim();

            if (string.IsNullOrEmpty(processName)) continue;

            // Generate one record per minute from start to end (exclusive)
            var current = new DateTime(start.Year, start.Month, start.Day, start.Hour, start.Minute, 0);
            var endMinute = new DateTime(end.Year, end.Month, end.Day, end.Hour, end.Minute, 0);
            if (current == endMinute) endMinute = current; // at least one record

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

            // Load existing records and collect their timestamps
            var existingTimestamps = new HashSet<string>();
            var existingLines = new List<string>();
            if (File.Exists(filePath))
            {
                foreach (var line in File.ReadAllLines(filePath, Encoding.UTF8))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    existingLines.Add(line);
                    // Extract timestamp (first field) for dedup
                    var comma = line.IndexOf(',');
                    if (comma > 0)
                        existingTimestamps.Add(line[..comma]);
                }
            }

            // Append only non-duplicate imported records
            int added = 0;
            foreach (var record in importedRecords)
            {
                var timeKey = record.Timestamp.ToString("HH:mm:ss");
                if (!existingTimestamps.Contains(timeKey))
                {
                    existingLines.Add(record.ToCsvLine());
                    existingTimestamps.Add(timeKey);
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

    private static bool TryParseDateTime(string s, out DateTime result)
    {
        // Handle formats like "2022/10/20 11:12" or "2022-10-20 11:12"
        string[] formats = [
            "yyyy/MM/dd HH:mm",
            "yyyy/MM/dd H:mm",
            "yyyy-MM-dd HH:mm",
            "yyyy-MM-dd H:mm",
            "yyyy/M/d HH:mm",
            "yyyy/M/d H:mm",
        ];
        return DateTime.TryParseExact(s, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out result);
    }
}
