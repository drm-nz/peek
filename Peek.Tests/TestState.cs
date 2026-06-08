using Microsoft.Extensions.Configuration;

namespace Peek.Tests;

public static class TestState
{
    public static readonly HttpClient OriginalClient = Program.Client;
    public static readonly string? OriginalSlackWebhook = Program._slackWebhook;
    public static readonly bool OriginalUseSlack = Program._useSlack;
    public static readonly int OriginalSlackReportInterval = Program._slackReportInterval;

    public static void RestoreDefaults()
    {
        Program.Client = OriginalClient;
        Program._slackWebhook = OriginalSlackWebhook ?? string.Empty;
        Program._useSlack = OriginalUseSlack;
        Program._slackReportInterval = OriginalSlackReportInterval;
    }

    public static IDisposable CaptureClient()
    {
        var saved = Program.Client;
        return new RestoreAction(() => Program.Client = saved);
    }

    public static IDisposable CaptureConfig()
    {
        var saved = Program._config;
        return new RestoreAction(() => Program._config = saved);
    }

    private class RestoreAction : IDisposable
    {
        private readonly Action _action;
        public RestoreAction(Action action) => _action = action;
        public void Dispose() => _action();
    }
}
