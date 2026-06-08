using System.Net;

namespace Peek.Tests;

public class SendSlackNotificationTests
{
    public SendSlackNotificationTests()
    {
        Log.Reset();
        TestState.RestoreDefaults();
        Program._useSlack = true;
        Program._slackWebhook = "https://hooks.slack.com/services/test";
        Program._slackChannel = "#notifications";
    }

    [Fact]
    public async Task no_webhook_skips_request()
    {
        Program._slackWebhook = string.Empty;
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK);
        Program.Client = new HttpClient(handler);

        await Program.SendSlackNotificationAsync(
            "https://example.com", 200, 500, DateTime.Now.AddDays(1), "");

        Assert.Empty(handler.ReceivedRequests);
    }

    [Fact]
    public async Task down_notification_sends_http_post()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK);
        Program.Client = new HttpClient(handler);

        await Program.SendSlackNotificationAsync(
            "https://example.com", 200, 500, DateTime.Now.AddDays(1), "");

        Assert.Single(handler.ReceivedRequests);
        Assert.Equal(HttpMethod.Post, handler.ReceivedRequests[0].Method);
        Assert.Equal("https://hooks.slack.com/services/test",
            handler.ReceivedRequests[0].RequestUri?.ToString());
    }

    [Fact]
    public async Task down_payload_contains_expected_fields()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK);
        Program.Client = new HttpClient(handler);

        await Program.SendSlackNotificationAsync(
            "https://example.com", 200, 500, DateTime.Now.AddDays(1), "");

        var body = await handler.ReceivedRequests.Single().Content!.ReadAsStringAsync();
        Assert.Contains("DOWN", body);
        Assert.Contains("danger", body);
        Assert.Contains("example.com", body);
        Assert.Contains("200", body);
        Assert.Contains("500", body);
        Assert.Contains("notifications", body);
    }

    [Fact]
    public async Task recovered_notification_sends_http_post()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK);
        Program.Client = new HttpClient(handler);

        await Program.SendSlackNotificationAsync(
            "https://example.com", 500, 200, DateTime.Now.AddDays(1), "");

        var body = await handler.ReceivedRequests.Single().Content!.ReadAsStringAsync();
        Assert.Contains("RECOVERED", body);
        Assert.Contains("good", body);
    }

    [Fact]
    public async Task information_notification_sends_when_cooldown_expired()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK);
        Program.Client = new HttpClient(handler);

        await Program.SendSlackNotificationAsync(
            "https://example.com", 200, 200,
            DateTime.Now.AddDays(-1), "certificate expiring");

        Assert.Single(handler.ReceivedRequests);
        var body = await handler.ReceivedRequests[0].Content!.ReadAsStringAsync();
        Assert.Contains("INFORMATION", body);
        Assert.Contains("warning", body);
    }

    [Fact]
    public async Task null_status_from_decision_skips_http_call()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK);
        Program.Client = new HttpClient(handler);

        await Program.SendSlackNotificationAsync(
            "https://example.com", 200, 200, DateTime.Now.AddDays(1), "");

        Assert.Empty(handler.ReceivedRequests);
    }

    [Fact]
    public async Task http_failure_during_post_is_caught()
    {
        var handler = new MockHttpMessageHandler(_ =>
            throw new HttpRequestException("Slack is down"));
        Program.Client = new HttpClient(handler);

        var logOutput = new StringWriter();
        Console.SetOut(logOutput);

        await Program.SendSlackNotificationAsync(
            "https://example.com", 200, 500, DateTime.Now.AddDays(1), "");

        Assert.Contains("Failed to send Slack notification", logOutput.ToString());
    }
}
