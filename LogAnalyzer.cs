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

    public static List<(DateTime Date, ActivityRecord Record)> ReadRecords(
        string logDirectory, DateTime startDate, DateTime endDate)
    {
        var results = new List<(DateTime, ActivityRecord)>();
        for (var d = startDate.Date; d <= endDate.Date; d = d.AddDays(1))
        {
            var path = Path.Combine(logDirectory, $"{d:yyyy-MM-dd}.csv");
            if (!File.Exists(path)) continue;
            foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    results.Add((d, ActivityRecord.FromCsvLine(line)));
                }
                catch { }
            }
        }
        return results;
    }

    public static List<AppUsageSummary> Aggregate(
        List<(DateTime Date, ActivityRecord Record)> records, int topN = 15)
    {
        int totalRecords = records.Count;
        if (totalRecords == 0) return [];

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
        List<(DateTime Date, ActivityRecord Record)> records)
    {
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

    public static string FormatTime(int minutes)
    {
        if (minutes >= 60)
            return $"{minutes / 60}h {minutes % 60:D2}m";
        return $"{minutes}m";
    }
}
