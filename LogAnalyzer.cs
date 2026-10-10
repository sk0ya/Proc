using System.Globalization;
using System.IO;
using System.Text;

namespace Proc;

public enum AnalysisPeriod { Day, Week, Month }

public record AppUsageSummary(
    string ProcessName,
    int TotalMinutes,
    double Percentage,
    List<TitleUsage> TitleBreakdown);

public record TitleUsage(string WindowTitle, int Minutes);

public record DailyUsage(DateTime Date, int Minutes);

public record DailySegment(string ProcessName, int Minutes);

public record DailyStack(DateTime Date, List<DailySegment> Segments)
{
    public int TotalMinutes => Segments.Sum(s => s.Minutes);
}

public record TimelineBlock(
    TimeSpan StartTime,
    TimeSpan EndTime,
    string ProcessName,
    string WindowTitle,
    int Minutes);

public static class LogAnalyzer
{
    public static (DateTime Start, DateTime End) GetDateRange(DateTime referenceDate, AnalysisPeriod period)
    {
        return period switch
        {
            AnalysisPeriod.Day => (referenceDate.Date, referenceDate.Date),
            AnalysisPeriod.Week => GetWeekRange(referenceDate),
            AnalysisPeriod.Month => (
                new DateTime(referenceDate.Year, referenceDate.Month, 1),
                new DateTime(referenceDate.Year, referenceDate.Month,
                    DateTime.DaysInMonth(referenceDate.Year, referenceDate.Month))),
            _ => (referenceDate.Date, referenceDate.Date)
        };
    }

    private static (DateTime, DateTime) GetWeekRange(DateTime date)
    {
        int diff = ((int)date.DayOfWeek - 1 + 7) % 7;
        var monday = date.Date.AddDays(-diff);
        return (monday, monday.AddDays(6));
    }

    public static string GetLogFilePath(string logDirectory, DateTime date) =>
        Path.Combine(logDirectory, $"{date:yyyy-MM-dd}.csv");

    public static List<(DateTime Date, ActivityRecord Record)> ReadRecords(
        string logDirectory, DateTime startDate, DateTime endDate)
    {
        var results = new List<(DateTime, ActivityRecord)>();
        for (var d = startDate.Date; d <= endDate.Date; d = d.AddDays(1))
        {
            foreach (var record in ReadLogFile(GetLogFilePath(logDirectory, d)))
                results.Add((d, record));
        }
        return results;
    }

    /// <summary>Reads one daily log file, skipping malformed lines. Missing file = no records.</summary>
    public static List<ActivityRecord> ReadLogFile(string path)
    {
        var records = new List<ActivityRecord>();
        if (!File.Exists(path)) return records;
        foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                records.Add(ActivityRecord.FromCsvLine(line));
            }
            catch
            {
                // skip malformed lines
            }
        }
        return records;
    }

    /// <summary>Deletes daily logs dated more than <paramref name="retentionDays"/> days before today.</summary>
    public static int DeleteOldLogs(string logDirectory, int retentionDays, DateTime today)
    {
        if (retentionDays <= 0 || !Directory.Exists(logDirectory)) return 0;
        var cutoff = today.Date.AddDays(-retentionDays);
        int deleted = 0;
        foreach (var path in Directory.EnumerateFiles(logDirectory, "????-??-??.csv"))
        {
            if (!DateTime.TryParseExact(Path.GetFileNameWithoutExtension(path), "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                continue;
            if (date >= cutoff) continue;
            try
            {
                File.Delete(path);
                deleted++;
            }
            catch (Exception ex)
            {
                ErrorLog.Write("DeleteOldLogs", ex);
            }
        }
        return deleted;
    }

    public static List<AppUsageSummary> Aggregate(
        List<(DateTime Date, ActivityRecord Record)> records, int topN = 15, bool includeIdle = false)
    {
        var filtered = includeIdle ? records : records.Where(r => !r.Record.IsIdle).ToList();
        int totalRecords = filtered.Count;
        if (totalRecords == 0) return [];
        records = filtered;

        var groups = records
            .GroupBy(r => r.Record.ProcessName)
            .Select(g => new
            {
                ProcessName = g.Key,
                TotalMinutes = g.Count(),
                Titles = g.GroupBy(r => r.Record.WindowTitle)
                    .Select(tg => new TitleUsage(tg.Key, tg.Count()))
                    .OrderByDescending(t => t.Minutes)
                    .ToList()
            })
            .OrderByDescending(x => x.TotalMinutes)
            .ToList();

        var top = groups.Take(topN).ToList();
        var rest = groups.Skip(topN).ToList();

        var summaries = top.Select(g => new AppUsageSummary(
            g.ProcessName,
            g.TotalMinutes,
            Math.Round(100.0 * g.TotalMinutes / totalRecords, 1),
            g.Titles)).ToList();

        if (rest.Count > 0)
        {
            int otherMinutes = rest.Sum(g => g.TotalMinutes);
            summaries.Add(new AppUsageSummary(
                "Other",
                otherMinutes,
                Math.Round(100.0 * otherMinutes / totalRecords, 1),
                rest.SelectMany(g => g.Titles).OrderByDescending(t => t.Minutes).ToList()));
        }

        return summaries;
    }

    public static List<DailyUsage> GetDailyBreakdown(
        List<(DateTime Date, ActivityRecord Record)> records, string processName)
    {
        return records
            .Where(r => r.Record.ProcessName == processName)
            .GroupBy(r => r.Date)
            .Select(g => new DailyUsage(g.Key, g.Count()))
            .OrderBy(d => d.Date)
            .ToList();
    }

    /// <summary>
    /// Minutes per group for every day in [start, end] (days without data have no segments).
    /// Segments are ordered by total usage over the whole range so stacks line up day to day.
    /// </summary>
    public static List<DailyStack> GetDailyStacks(
        List<(DateTime Date, ActivityRecord Record)> records, DateTime start, DateTime end, bool includeIdle = false)
    {
        if (!includeIdle)
            records = records.Where(r => !r.Record.IsIdle).ToList();

        var order = records
            .GroupBy(r => r.Record.ProcessName)
            .OrderByDescending(g => g.Count())
            .Select((g, i) => (g.Key, i))
            .ToDictionary(x => x.Key, x => x.i);

        var byDate = records.ToLookup(r => r.Date.Date);
        var stacks = new List<DailyStack>();
        for (var d = start.Date; d <= end.Date; d = d.AddDays(1))
        {
            var segments = byDate[d]
                .GroupBy(r => r.Record.ProcessName)
                .Select(g => new DailySegment(g.Key, g.Count()))
                .OrderBy(s => order[s.ProcessName])
                .ToList();
            stacks.Add(new DailyStack(d, segments));
        }
        return stacks;
    }

    public static string GetPeriodLabel(DateTime referenceDate, AnalysisPeriod period)
    {
        var (start, end) = GetDateRange(referenceDate, period);
        return period switch
        {
            AnalysisPeriod.Day => referenceDate.ToString("yyyy/MM/dd (ddd)"),
            AnalysisPeriod.Week =>
                $"{start:MM/dd} - {end:MM/dd} (Week {ISOWeek.GetWeekOfYear(start)})",
            AnalysisPeriod.Month => referenceDate.ToString("yyyy/MM"),
            _ => referenceDate.ToString("d")
        };
    }

    public static DateTime Navigate(DateTime current, AnalysisPeriod period, int direction)
    {
        return period switch
        {
            AnalysisPeriod.Day => current.AddDays(direction),
            AnalysisPeriod.Week => current.AddDays(7 * direction),
            AnalysisPeriod.Month => current.AddMonths(direction),
            _ => current
        };
    }

    public static List<TimelineBlock> BuildTimeline(
        List<(DateTime Date, ActivityRecord Record)> records, bool includeIdle = false)
    {
        if (!includeIdle)
            records = records.Where(r => !r.Record.IsIdle).ToList();
        if (records.Count == 0) return [];

        var sorted = records
            .OrderBy(r => r.Record.Timestamp.TimeOfDay)
            .ToList();

        var blocks = new List<TimelineBlock>();
        var current = sorted[0];
        var blockStart = current.Record.Timestamp.TimeOfDay;
        var blockEnd = blockStart.Add(TimeSpan.FromMinutes(1));
        string blockProcess = current.Record.ProcessName;
        string blockTitle = current.Record.WindowTitle;
        int blockMinutes = 1;

        for (int i = 1; i < sorted.Count; i++)
        {
            var rec = sorted[i].Record;
            var time = rec.Timestamp.TimeOfDay;
            var gap = time - blockEnd;

            // Merge if same process and gap <= 2 minutes (accounts for missing entries)
            if (rec.ProcessName == blockProcess && gap <= TimeSpan.FromMinutes(2))
            {
                blockEnd = time.Add(TimeSpan.FromMinutes(1));
                blockMinutes++;
                // Keep the most recent title
                blockTitle = rec.WindowTitle;
            }
            else
            {
                blocks.Add(new TimelineBlock(blockStart, blockEnd, blockProcess, blockTitle, blockMinutes));
                blockStart = time;
                blockEnd = time.Add(TimeSpan.FromMinutes(1));
                blockProcess = rec.ProcessName;
                blockTitle = rec.WindowTitle;
                blockMinutes = 1;
            }
        }
        blocks.Add(new TimelineBlock(blockStart, blockEnd, blockProcess, blockTitle, blockMinutes));

        return blocks;
    }

    /// <summary>Formats a time of day as HH:mm (24:00 for end of day).</summary>
    public static string FormatClock(TimeSpan time) =>
        $"{(int)time.TotalHours:D2}:{time.Minutes:D2}";

    public static string FormatTime(int minutes)
    {
        if (minutes >= 60)
            return $"{minutes / 60}h {minutes % 60:D2}m";
        return $"{minutes}m";
    }
}
