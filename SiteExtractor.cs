namespace Proc;

/// <summary>
/// Heuristically extracts the site name from a browser window title,
/// e.g. "Some video - YouTube - Google Chrome" -> "YouTube".
/// </summary>
public static class SiteExtractor
{
    private static readonly HashSet<string> Browsers = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "firefox", "brave", "opera", "opera_gx", "vivaldi", "arc", "waterfox", "librewolf", "zen", "floorp"
    };

    private static readonly string[] Separators = [" - ", " — ", " – ", " | "];

    public const string Unknown = "(other pages)";

    public static bool IsBrowser(string processName) => Browsers.Contains(processName);

    public static string Extract(string title)
    {
        var parts = SplitTitle(title);

        // Drop the browser name (and Edge's profile segment like "Personal", "Work")
        if (parts.Count > 0 && LooksLikeBrowserName(parts[^1]))
        {
            parts.RemoveAt(parts.Count - 1);
            if (parts.Count > 1 && IsProfileSegment(parts[^1]))
                parts.RemoveAt(parts.Count - 1);
        }

        // Sites put their name last ("Page - Site"); a lone segment is usually the site's home page
        return parts.Count > 0 ? parts[^1] : Unknown;
    }

    private static List<string> SplitTitle(string title)
    {
        var parts = new List<string> { title };
        foreach (var sep in Separators)
            parts = parts.SelectMany(p => p.Split(sep)).ToList();
        return parts.Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
    }

    private static bool LooksLikeBrowserName(string s)
    {
        // Edge inserts a zero-width space in "Microsoft​ Edge"
        var n = s.Replace("​", "");
        return n.EndsWith("Chrome", StringComparison.OrdinalIgnoreCase)
            || n.EndsWith("Edge", StringComparison.OrdinalIgnoreCase)
            || n.EndsWith("Firefox", StringComparison.OrdinalIgnoreCase)
            || n.EndsWith("Brave", StringComparison.OrdinalIgnoreCase)
            || n.EndsWith("Opera", StringComparison.OrdinalIgnoreCase)
            || n.EndsWith("Vivaldi", StringComparison.OrdinalIgnoreCase)
            || n.Equals("Arc", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsProfileSegment(string s) =>
        s.StartsWith("Personal", StringComparison.OrdinalIgnoreCase)
        || s.StartsWith("Work", StringComparison.OrdinalIgnoreCase)
        || s.StartsWith("Profile", StringComparison.OrdinalIgnoreCase)
        || s.StartsWith("InPrivate", StringComparison.OrdinalIgnoreCase);
}
