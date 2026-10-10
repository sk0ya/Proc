using System.IO;
using System.Text;
using Proc;
using Xunit;

namespace Proc.Tests;

public sealed class ImportHelperTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ProcTests_" + Guid.NewGuid().ToString("N"));

    public ImportHelperTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, true);

    private string WriteImportFile(params string[] rows)
    {
        var path = Path.Combine(_dir, "import.csv");
        File.WriteAllLines(path, ["Name,Start,End,Duration,Process", .. rows], Encoding.UTF8);
        return path;
    }

    [Fact]
    public void Import_SkipsMinutesAlreadyLoggedWithSeconds()
    {
        var logDir = Path.Combine(_dir, "logs");
        Directory.CreateDirectory(logDir);
        // Proc's own records carry seconds
        File.WriteAllLines(Path.Combine(logDir, "2026-01-05.csv"), ["10:15:37,code,editor"], Encoding.UTF8);

        var import = WriteImportFile("\"doc\",\"2026/01/05 10:15\",\"2026/01/05 10:17\",\"2m\",\"winword\"");

        int added = ImportHelper.Import(import, logDir);

        Assert.Equal(2, added);
        var records = LogAnalyzer.ReadLogFile(Path.Combine(logDir, "2026-01-05.csv"));
        Assert.Equal(3, records.Count);
        Assert.Single(records, r => r.ProcessName == "code");
        Assert.DoesNotContain(records, r => r.ProcessName == "winword" && r.Timestamp.TimeOfDay == new TimeSpan(10, 15, 0));
    }

    [Fact]
    public void Import_IsIdempotent()
    {
        var logDir = Path.Combine(_dir, "logs");
        var import = WriteImportFile("\"doc\",\"2026/01/05 10:00\",\"2026/01/05 10:04\",\"4m\",\"winword\"");

        Assert.Equal(5, ImportHelper.Import(import, logDir));
        Assert.Equal(0, ImportHelper.Import(import, logDir));
    }

    [Fact]
    public void Import_SplitsAcrossMidnight()
    {
        var logDir = Path.Combine(_dir, "logs");
        var import = WriteImportFile("\"late\",\"2026-01-05 23:59\",\"2026-01-06 00:01\",\"2m\",\"code\"");

        Assert.Equal(3, ImportHelper.Import(import, logDir));
        Assert.Single(LogAnalyzer.ReadLogFile(Path.Combine(logDir, "2026-01-05.csv")));
        Assert.Equal(2, LogAnalyzer.ReadLogFile(Path.Combine(logDir, "2026-01-06.csv")).Count);
    }
}
