using Proc;
using Xunit;

namespace Proc.Tests;

public class ActivityRecordTests
{
    [Theory]
    [InlineData("plain title")]
    [InlineData("a, b, c")]
    [InlineData("say \"hi\"")]
    [InlineData("line1\rline2")]
    [InlineData("")]
    public void CsvRoundTrip_PreservesTitle(string title)
    {
        var record = new ActivityRecord(new DateTime(2026, 1, 5, 9, 3, 7), "code", title);

        var parsed = ActivityRecord.FromCsvLine(record.ToCsvLine());

        Assert.Equal("code", parsed.ProcessName);
        Assert.Equal(title, parsed.WindowTitle);
        Assert.Equal(new TimeSpan(9, 3, 7), parsed.Timestamp.TimeOfDay);
        Assert.False(parsed.IsIdle);
    }

    [Fact]
    public void IdleRecord_RoundTrips()
    {
        var record = new ActivityRecord(new DateTime(2026, 1, 5, 12, 0, 0), "Idle", "", true);

        var parsed = ActivityRecord.FromCsvLine(record.ToCsvLine());

        Assert.True(parsed.IsIdle);
        Assert.Equal("12:00:00,Idle,,idle", record.ToCsvLine());
    }

    [Fact]
    public void FromCsvLine_RejectsTooFewColumns()
    {
        Assert.Throws<FormatException>(() => ActivityRecord.FromCsvLine("10:00:00,code"));
    }

    [Fact]
    public void FromCsvLine_RejectsBadTime()
    {
        Assert.Throws<FormatException>(() => ActivityRecord.FromCsvLine("not-a-time,code,title"));
    }
}
