using Proc;
using Xunit;

namespace Proc.Tests;

public class CategoryRulesTests
{
    private static readonly CategoryRules Rules = CategoryRules.Parse(
    [
        "# comment",
        "Work = devenv, Code.exe, title:Jira",
        "Chat = slack",
        "broken line without equals",
        "Video = title:YouTube",
    ]);

    [Theory]
    [InlineData("devenv", "", "Work")]
    [InlineData("CODE", "", "Work")]              // case-insensitive, ".exe" stripped from the rule
    [InlineData("chrome", "PROJ-1 - Jira", "Work")] // title match
    [InlineData("slack", "", "Chat")]
    [InlineData("chrome", "cats - YouTube", "Video")]
    [InlineData("notepad", "", CategoryRules.Uncategorized)]
    public void Match(string process, string title, string expected) =>
        Assert.Equal(expected, Rules.Match(process, title));

    [Fact]
    public void FirstMatchingLineWins()
    {
        var rules = CategoryRules.Parse(["A = title:x", "B = chrome"]);
        Assert.Equal("A", rules.Match("chrome", "x"));
    }

    [Fact]
    public void EmptyRules() => Assert.True(CategoryRules.Parse(["", "  "]).IsEmpty);
}

public class SiteExtractorTests
{
    [Theory]
    [InlineData("Some video - YouTube - Google Chrome", "YouTube")]
    [InlineData("Pull requests · sk0ya/Proc - GitHub - Personal - Microsoft​ Edge", "GitHub")]
    [InlineData("Inbox | Mail - Mozilla Firefox", "Mail")]
    [InlineData("YouTube - Google Chrome", "YouTube")]
    [InlineData("Google Chrome", SiteExtractor.Unknown)]
    public void Extract(string title, string expected) =>
        Assert.Equal(expected, SiteExtractor.Extract(title));

    [Fact]
    public void IsBrowser()
    {
        Assert.True(SiteExtractor.IsBrowser("msedge"));
        Assert.False(SiteExtractor.IsBrowser("code"));
    }
}

public class ReportExporterTests
{
    private static readonly List<AppUsageSummary> Summaries =
    [
        new("code", 90, 75.0, [new TitleUsage("main.cs, edited", 60), new TitleUsage("", 30)]),
        new("a|b", 30, 25.0, [new TitleUsage("x", 30)]),
    ];

    [Fact]
    public void ToCsv_OneRowPerTitle_Escaped()
    {
        var lines = ReportExporter.ToCsv(Summaries, "App").TrimEnd().Split(Environment.NewLine);

        Assert.Equal("App,Title,Minutes", lines[0]);
        Assert.Equal("code,\"main.cs, edited\",60", lines[1]);
        Assert.Equal(4, lines.Length);
    }

    [Fact]
    public void ToMarkdown_HasTotalsAndEscapedTable()
    {
        var md = ReportExporter.ToMarkdown(Summaries, "App", "2026/01/05");

        Assert.Contains("Total: **2h 00m**", md);
        Assert.Contains("| a\\|b | 30m | 25% |", md);
        Assert.Contains("- (no title) — 30m", md);
    }
}
