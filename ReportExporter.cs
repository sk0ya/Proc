using System.Text;

namespace Proc;

/// <summary>Formats aggregated usage as CSV (one row per group + title) or a Markdown report.</summary>
public static class ReportExporter
{
    public static string ToCsv(List<AppUsageSummary> summaries, string groupLabel)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{CsvUtil.Escape(groupLabel)},Title,Minutes");
        foreach (var s in summaries)
            foreach (var t in s.TitleBreakdown)
                sb.AppendLine($"{CsvUtil.Escape(s.ProcessName)},{CsvUtil.Escape(t.WindowTitle)},{t.Minutes}");
        return sb.ToString();
    }

    public static string ToMarkdown(List<AppUsageSummary> summaries, string groupLabel, string periodText, int titlesPerGroup = 10)
    {
        var sb = new StringBuilder();
        int total = summaries.Sum(s => s.TotalMinutes);
        sb.AppendLine($"# Proc report: {periodText}");
        sb.AppendLine();
        sb.AppendLine($"Total: **{LogAnalyzer.FormatTime(total)}**");
        sb.AppendLine();
        sb.AppendLine($"| {groupLabel} | Time | % |");
        sb.AppendLine("|---|---:|---:|");
        foreach (var s in summaries)
            sb.AppendLine($"| {EscapeCell(s.ProcessName)} | {LogAnalyzer.FormatTime(s.TotalMinutes)} | {s.Percentage}% |");

        foreach (var s in summaries)
        {
            sb.AppendLine();
            sb.AppendLine($"## {s.ProcessName} ({LogAnalyzer.FormatTime(s.TotalMinutes)})");
            sb.AppendLine();
            foreach (var t in s.TitleBreakdown.Take(titlesPerGroup))
            {
                var title = string.IsNullOrWhiteSpace(t.WindowTitle) ? "(no title)" : t.WindowTitle;
                sb.AppendLine($"- {title} — {LogAnalyzer.FormatTime(t.Minutes)}");
            }
            if (s.TitleBreakdown.Count > titlesPerGroup)
                sb.AppendLine($"- … and {s.TitleBreakdown.Count - titlesPerGroup} more");
        }
        return sb.ToString();
    }

    private static string EscapeCell(string s) => s.Replace("|", "\\|");
}
