namespace Proc;

/// <summary>
/// Maps records to user-defined categories. One rule per line:
/// <c>Category = pattern, pattern, ...</c>
/// A plain pattern matches the process name exactly (case-insensitive);
/// <c>title:text</c> matches when the window title contains the text.
/// The first matching line wins.
/// </summary>
public class CategoryRules
{
    public const string Uncategorized = "Uncategorized";

    private readonly List<(string Category, HashSet<string> Processes, List<string> TitleParts)> _rules = [];

    public bool IsEmpty => _rules.Count == 0;

    public static CategoryRules Parse(IEnumerable<string> lines)
    {
        var rules = new CategoryRules();
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;

            var category = line[..eq].Trim();
            if (category.Length == 0) continue;

            var processes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var titleParts = new List<string>();
            foreach (var p in line[(eq + 1)..].Split(','))
            {
                var pattern = p.Trim();
                if (pattern.StartsWith("title:", StringComparison.OrdinalIgnoreCase))
                {
                    var text = pattern["title:".Length..].Trim();
                    if (text.Length > 0) titleParts.Add(text);
                }
                else if (pattern.Length > 0)
                {
                    processes.Add(pattern.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? pattern[..^4] : pattern);
                }
            }
            if (processes.Count > 0 || titleParts.Count > 0)
                rules._rules.Add((category, processes, titleParts));
        }
        return rules;
    }

    public string Match(string processName, string windowTitle)
    {
        foreach (var (category, processes, titleParts) in _rules)
        {
            if (processes.Contains(processName)) return category;
            foreach (var part in titleParts)
                if (windowTitle.Contains(part, StringComparison.OrdinalIgnoreCase)) return category;
        }
        return Uncategorized;
    }
}
