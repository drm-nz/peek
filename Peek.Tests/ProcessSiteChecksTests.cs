using System.Net;
using System.Text;
using LiteDB;
using Microsoft.Extensions.Configuration;

namespace Peek.Tests;

public class ProcessSiteChecksTests : IDisposable
{
    private readonly LiteDatabase _db;
    private readonly ILiteCollection<SiteCheck> _collection;
    private readonly MockHttpMessageHandler _httpHandler;
    private readonly string _logDir;
    private readonly IDisposable _configRestore;

    public ProcessSiteChecksTests()
    {
        _logDir = Path.Combine(Path.GetTempPath(), "peek-test-" + Guid.NewGuid());
        Log.Reset();
        Log.Initialise(_logDir);

        _db = new LiteDatabase(new MemoryStream());
        _collection = _db.GetCollection<SiteCheck>("SiteCheckStates");

        _httpHandler = new MockHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Hello World") });
        Program.Client = new HttpClient(_httpHandler);

        _configRestore = TestState.CaptureConfig();
        Program._useSlack = false;
        Program._slackWebhook = string.Empty;
        Program._config = BuildConfig("""
        {
            "UseSlack": false,
            "MaxConcurrency": 1,
            "SiteChecks": []
        }
        """);
        Program.RetryDelayMs = 1;
    }

    public void Dispose()
    {
        _db.Dispose();
        _configRestore.Dispose();
        TestState.RestoreDefaults();
        Log.Reset();
        if (Directory.Exists(_logDir))
            Directory.Delete(_logDir, true);
    }

    private static IConfigurationRoot BuildConfig(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return new ConfigurationBuilder().AddJsonStream(stream).Build();
    }

    private void SeedSite(string url, int interval, int lastState = 200, int? pastDueSecs = null)
    {
        _collection.Insert(new SiteCheck
        {
            URL = url,
            Interval = interval,
            SearchString = "*",
            LastState = lastState,
            Message = string.Empty,
            NextCheck = pastDueSecs.HasValue
                ? DateTime.Now.AddSeconds(-pastDueSecs.Value)
                : DateTime.Now.AddSeconds(interval + 60),
            ConfigUpdated = DateTime.Now,
            NextNotification = DateTime.Now
        });
    }

    [Fact]
    public async Task no_due_sites_returns_zero()
    {
        SeedSite("https://example.com", 60);
        var (total, failed) = await Program.ProcessSiteChecksAsync(_collection);
        Assert.Equal(0, total);
        Assert.Equal(0, failed);
    }

    [Fact]
    public async Task single_due_site_success()
    {
        SeedSite("https://example.com", 60, pastDueSecs: 10);
        var (total, failed) = await Program.ProcessSiteChecksAsync(_collection);
        Assert.Equal(1, total);
        Assert.Equal(0, failed);
        var record = _collection.FindOne(Query.EQ("URL", "https://example.com"));
        Assert.Equal(200, record.LastState);
    }

    [Fact]
    public async Task single_due_site_failure()
    {
        SeedSite("https://example.com", 60, pastDueSecs: 10);
        Program.Client = new HttpClient(new MockHttpMessageHandler(_ =>
            throw new HttpRequestException("Network error")));
        var (total, failed) = await Program.ProcessSiteChecksAsync(_collection);
        Assert.Equal(1, total);
        Assert.Equal(1, failed);
        var record = _collection.FindOne(Query.EQ("URL", "https://example.com"));
        Assert.Equal(410, record.LastState);
    }

    [Fact]
    public async Task updates_next_check_in_database()
    {
        SeedSite("https://example.com", 30, pastDueSecs: 10);
        await Program.ProcessSiteChecksAsync(_collection);
        var record = _collection.FindOne(Query.EQ("URL", "https://example.com"));
        Assert.True(record.NextCheck > DateTime.Now.AddSeconds(25));
        Assert.True(record.NextCheck <= DateTime.Now.AddSeconds(35));
    }

    [Fact]
    public async Task only_processes_due_sites()
    {
        SeedSite("https://due.com", 60, pastDueSecs: 10);
        SeedSite("https://not-due.com", 60, pastDueSecs: -10);
        var (total, failed) = await Program.ProcessSiteChecksAsync(_collection);
        Assert.Equal(1, total);
    }

    [Fact]
    public async Task multiple_due_sites_all_succeed()
    {
        SeedSite("https://a.com", 60, pastDueSecs: 10);
        SeedSite("https://b.com", 60, pastDueSecs: 10);
        SeedSite("https://c.com", 60, pastDueSecs: 10);
        var (total, failed) = await Program.ProcessSiteChecksAsync(_collection);
        Assert.Equal(3, total);
        Assert.Equal(0, failed);
    }

    [Fact]
    public async Task mixed_results_counts_failures()
    {
        SeedSite("https://good.com", 60, pastDueSecs: 10);
        SeedSite("https://bad.com", 60, pastDueSecs: 10);
        Program.Client = new HttpClient(new MockHttpMessageHandler(req =>
            req.RequestUri?.Host == "bad.com"
                ? throw new HttpRequestException("fail")
                : new HttpResponseMessage(HttpStatusCode.OK)));
        var (total, failed) = await Program.ProcessSiteChecksAsync(_collection);
        Assert.Equal(2, total);
        Assert.Equal(1, failed);
    }

    [Fact]
    public async Task with_slack_enabled_sends_notification_on_failure()
    {
        Program._useSlack = true;
        Program._slackWebhook = "https://hooks.slack.com/services/test";
        Program._slackChannel = "#alerts";
        SeedSite("https://example.com", 60, lastState: 200, pastDueSecs: 10);

        var failing = new MockHttpMessageHandler(HttpStatusCode.InternalServerError);
        Program.Client = new HttpClient(failing);

        await Program.ProcessSiteChecksAsync(_collection);

        Assert.Equal(2, failing.ReceivedRequests.Count);
    }

    [Fact]
    public async Task with_slack_enabled_does_not_send_when_state_unchanged()
    {
        Program._useSlack = true;
        Program._slackWebhook = "https://hooks.slack.com/services/test";
        SeedSite("https://example.com", 60, lastState: 200, pastDueSecs: 10);
        await Program.ProcessSiteChecksAsync(_collection);
        Assert.Single(_httpHandler.ReceivedRequests);
    }

    [Fact]
    public async Task concurrent_mode_processes_all_due_sites()
    {
        Program._config = BuildConfig("""
        {
            "UseSlack": false,
            "MaxConcurrency": 5,
            "SiteChecks": []
        }
        """);
        SeedSite("https://a.com", 60, pastDueSecs: 10);
        SeedSite("https://b.com", 60, pastDueSecs: 10);
        SeedSite("https://c.com", 60, pastDueSecs: 10);
        var (total, failed) = await Program.ProcessSiteChecksAsync(_collection);
        Assert.Equal(3, total);
        Assert.Equal(0, failed);
    }
}
