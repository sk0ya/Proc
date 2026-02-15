namespace Proc;

public record ActivityRecord(DateTime Timestamp, string ProcessName, string WindowTitle)
{
    public string ToCsvLine() => $"{Timestamp:HH:mm:ss},{Escape(ProcessName)},{Escape(WindowTitle)}";

    public static ActivityRecord FromCsvLine(string line)
    {
        var parts = ParseCsvLine(line);
        if (parts.Count < 3) throw new FormatException("Invalid CSV line");
        return new ActivityRecord(
            DateTime.Parse(parts[0]),
            parts[1],
            parts[2]);
    }

    private static string Escape(string s)
    {
        if (s.Contains(',') || s.Contains('"') || s.Contains('\n'))
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }

    private static List<string> ParseCsvLine(string line)
    {
        var result = new List<string>();
        bool inQuote = false;
        var current = new System.Text.StringBuilder();
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQuote)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuote = false;
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
            else
            {
                if (c == '"') inQuote = true;
                else if (c == ',')
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                else current.Append(c);
            }
        }
        result.Add(current.ToString());
        return result;
    }
}
