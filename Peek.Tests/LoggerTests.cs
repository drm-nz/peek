namespace Peek.Tests;

public class LoggerTests : IDisposable
{
    private readonly string _logDir;
    private readonly string _captured;
    private readonly TextWriter _originalOut;

    public LoggerTests()
    {
        _logDir = Path.Combine(Path.GetTempPath(), "peek-test-logs-" + Guid.NewGuid());
        _originalOut = Console.Out;
        _captured = Path.Combine(_logDir, $"peek-{DateTime.Now:yyyy-MM-dd}.log");

        Log.Initialise(_logDir);
    }

    public void Dispose()
    {
        Console.SetOut(_originalOut);
        Log.Reset();
        if (Directory.Exists(_logDir))
            Directory.Delete(_logDir, true);
    }

    [Fact]
    public void Initialise_creates_directory()
    {
        Assert.True(Directory.Exists(_logDir));
    }

    [Fact]
    public void Info_writes_to_console_and_file()
    {
        var sw = new StringWriter();
        Console.SetOut(sw);

        Log.Info("hello world");

        var consoleOut = sw.ToString();
        Assert.Contains("[INFO]", consoleOut);
        Assert.Contains("hello world", consoleOut);

        Assert.True(File.Exists(_captured));
        var fileContent = File.ReadAllText(_captured);
        Assert.Contains("[INFO]", fileContent);
        Assert.Contains("hello world", fileContent);
    }

    [Fact]
    public void Warn_writes_to_console_and_file()
    {
        var sw = new StringWriter();
        Console.SetOut(sw);

        Log.Warn("warning test");

        var consoleOut = sw.ToString();
        Assert.Contains("[WARN]", consoleOut);
        Assert.Contains("warning test", consoleOut);

        Assert.True(File.Exists(_captured));
        var fileContent = File.ReadAllText(_captured);
        Assert.Contains("[WARN]", fileContent);
    }

    [Fact]
    public void Error_writes_to_console_and_file()
    {
        var sw = new StringWriter();
        Console.SetOut(sw);

        Log.Error("error test");

        var consoleOut = sw.ToString();
        Assert.Contains("[ERROR]", consoleOut);
        Assert.Contains("error test", consoleOut);

        Assert.True(File.Exists(_captured));
        var fileContent = File.ReadAllText(_captured);
        Assert.Contains("[ERROR]", fileContent);
    }

    [Fact]
    public void multiple_messages_append_to_file()
    {
        Log.Info("first");
        Log.Warn("second");

        var fileContent = File.ReadAllText(_captured);
        var lines = fileContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains(lines, l => l.Contains("[INFO]") && l.Contains("first"));
        Assert.Contains(lines, l => l.Contains("[WARN]") && l.Contains("second"));
    }

    [Fact]
    public void log_line_contains_timestamp()
    {
        var sw = new StringWriter();
        Console.SetOut(sw);

        Log.Info("timestamp check");

        var output = sw.ToString();
        Assert.Matches(@"\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}", output);
    }
}
