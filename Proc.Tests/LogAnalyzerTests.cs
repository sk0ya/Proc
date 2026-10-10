using System.IO;
using Proc;
using Xunit;

namespace Proc.Tests;

public class LogAnalyzerTests
{
    private static readonly DateTime Day = new(2026, 1, 5);

    private static (DateTime, ActivityRecord) Rec(int hour, int minute, string process, string title = "", bool idle = false, DateTime? date = null)
    {
        var d = date ?? Day;
        return (d, new ActivityRecord(d.AddHours(hour).AddMinutes(minute), process, title, idle));
    }

    [Fact]
    public void Aggregate_ExcludesIdleByDefault_AndComputesPercentages()
    {
        var records = new List<(DateTime, ActivityRecord)>
        {
            Rec(9, 0, "code"), Rec(9, 1, "code"), Rec(9, 2, "code"),
            Rec(9, 3, "chrome"),
            Rec(9, 4, "Idle", idle: true),
        };

        var result = LogAnalyzer.Aggregate(records);

        Assert.Equal(2, result.Count);
        Assert.Equal("code", result[0].ProcessName);
        Assert.Equal(3, result[0].TotalMinutes);
        Assert.Equal(75.0, result[0].Percentage);
        Assert.Equal(3, LogAnalyzer.Aggregate(records, includeIdle: true).Count);
    }

    [Fact]
    public void Aggregate_GroupsTailIntoOther()
    {
        var records = Enumerable.Range(0, 5).Select(i => Rec(9, i, $"app{i}")).ToList();

        var result = LogAnalyzer.Aggregate(records, topN: 3);

        Assert.Equal(4, result.Count);
        Assert.Equal("Other", result[^1].ProcessName);
        Assert.Equal(2, result[^1].TotalMinutes);
    }

    [Fact]
    public void BuildTimeline_MergesSameProcessAcrossSmallGaps()
    {
        var records = new List<(DateTime, ActivityRecord)>
        {
            Rec(9, 0, "code"), Rec(9, 1, "code"), Rec(9, 3, "code"), // 1-minute gap: merged
            Rec(9, 4, "chrome"),
            Rec(9, 10, "chrome"), // large gap: new block
        };

        var blocks = LogAnalyzer.BuildTimeline(records);

        Assert.Equal(3, blocks.Count);
        Assert.Equal(("code", 3), (blocks[0].ProcessName, blocks[0].Minutes));
        Assert.Equal(new TimeSpan(9, 4, 0), blocks[0].EndTime);
        Assert.Equal(new TimeSpan(9, 10, 0), blocks[2].StartTime);
    }

    [Fact]
    public void GetDailyStacks_CoversEveryDayInRange()
    {
        var records = new List<(DateTime, ActivityRecord)>
        {
            Rec(9, 0, "code"), Rec(9, 1, "code"), Rec(9, 2, "chrome"),
            Rec(9, 0, "chrome", date: Day.AddDays(2)),
        };

        var stacks = LogAnalyzer.GetDailyStacks(records, Day, Day.AddDays(3));

        Assert.Equal(4, stacks.Count);
        Assert.Equal(3, stacks[0].TotalMinutes);
        Assert.Equal("code", stacks[0].Segments[0].ProcessName); // most used overall goes first
        Assert.Empty(stacks[1].Segments);
        Assert.Equal(1, stacks[2].TotalMinutes);
    }

    [Fact]
    public void DeleteOldLogs_RemovesOnlyDatedFilesBeforeCutoff()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ProcTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            foreach (var name in new[] { "2026-01-01.csv", "2026-01-09.csv", "2026-01-10.csv", "notes.csv" })
                File.WriteAllText(Path.Combine(dir, name), "");

            int deleted = LogAnalyzer.DeleteOldLogs(dir, 1, new DateTime(2026, 1, 10));

            Assert.Equal(1, deleted);
            Assert.False(File.Exists(Path.Combine(dir, "2026-01-01.csv")));
            Assert.True(File.Exists(Path.Combine(dir, "2026-01-09.csv")));
            Assert.True(File.Exists(Path.Combine(dir, "notes.csv")));
            Assert.Equal(0, LogAnalyzer.DeleteOldLogs(dir, 0, new DateTime(2030, 1, 1)));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void GetDateRange_WeekStartsOnMonday()
    {
        var (start, end) = LogAnalyzer.GetDateRange(new DateTime(2026, 1, 4), AnalysisPeriod.Week); // Sunday

        Assert.Equal(new DateTime(2025, 12, 29), start);
        Assert.Equal(new DateTime(2026, 1, 4), end);
    }

    [Theory]
    [InlineData(0, "0m")]
    [InlineData(59, "59m")]
    [InlineData(60, "1h 00m")]
    [InlineData(125, "2h 05m")]
    public void FormatTime(int minutes, string expected) =>
        Assert.Equal(expected, LogAnalyzer.FormatTime(minutes));
}
