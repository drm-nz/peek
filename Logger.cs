using System;
using System.IO;

namespace Peek;

public static class Log
{
    private static string? _logDir;
    private static readonly object Lock = new();

    public static void Initialise(string? logDir = null)
    {
        _logDir = logDir ?? Path.Combine(Environment.CurrentDirectory, "logs");
        if (_logDir != null)
            Directory.CreateDirectory(_logDir);
    }

    public static void Reset()
    {
        _logDir = null;
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message) => Write("ERROR", message);

    private static void Write(string level, string message)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var line = $"{timestamp} [{level}] {message}";
        Console.WriteLine(line);

        if (_logDir == null) return;

        lock (Lock)
        {
            var path = Path.Combine(_logDir, $"peek-{DateTime.Now:yyyy-MM-dd}.log");
            File.AppendAllText(path, line + Environment.NewLine);
        }
    }
}
