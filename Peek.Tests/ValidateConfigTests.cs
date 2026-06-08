using System.Text;
using Microsoft.Extensions.Configuration;

namespace Peek.Tests;

public class ValidateConfigTests : IDisposable
{
    private readonly string _originalEnv;
    private readonly bool _originalUseSlack;
    private readonly int _originalSlackInterval;

    public ValidateConfigTests()
    {
        Log.Reset();
        _originalEnv = Environment.GetEnvironmentVariable("PEEK_SLACK_WEBHOOK_URL") ?? "";
        _originalUseSlack = Program._useSlack;
        _originalSlackInterval = Program._slackReportInterval;
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("PEEK_SLACK_WEBHOOK_URL", _originalEnv);
        Program._useSlack = _originalUseSlack;
        Program._slackReportInterval = _originalSlackInterval;
    }

    private static IConfigurationRoot BuildConfig(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return new ConfigurationBuilder().AddJsonStream(stream).Build();
    }

    [Fact]
    public void valid_config_returns_true()
    {
        Program._config = BuildConfig("""
        {
            "UseSlack": false,
            "SlackReportInterval": 14400,
            "SiteChecks": [{ "url": "https://example.com", "interval": 60 }]
        }
        """);
        Assert.True(Program.ValidateConfig());
    }

    [Fact]
    public void empty_site_checks_returns_false()
    {
        Program._config = BuildConfig("""{ "SiteChecks": [] }""");
        Assert.False(Program.ValidateConfig());
    }

    [Fact]
    public void slack_enabled_with_env_var_succeeds()
    {
        Environment.SetEnvironmentVariable("PEEK_SLACK_WEBHOOK_URL", "https://hooks.slack.com/services/env-test");
        Program._config = BuildConfig("""
        {
            "UseSlack": true,
            "SiteChecks": [{ "url": "https://example.com", "interval": 60 }]
        }
        """);
        Assert.True(Program.ValidateConfig());
        Assert.True(Program._useSlack);
    }

    [Fact]
    public void slack_enabled_without_webhook_returns_false()
    {
        Program._config = BuildConfig("""
        {
            "UseSlack": true,
            "SiteChecks": [{ "url": "https://example.com", "interval": 60 }]
        }
        """);
        Assert.False(Program.ValidateConfig());
    }

    [Fact]
    public void sets_static_fields_from_config()
    {
        Program._config = BuildConfig("""
        {
            "UseSlack": false,
            "SlackReportInterval": 14400,
            "SiteChecks": [{ "url": "https://example.com", "interval": 60 }]
        }
        """);
        Program.ValidateConfig();
        Assert.False(Program._useSlack);
        Assert.Equal(14400, Program._slackReportInterval);
    }

    [Fact]
    public void sets_static_fields_from_slack_config()
    {
        Environment.SetEnvironmentVariable("PEEK_SLACK_WEBHOOK_URL", "https://hooks.slack.com/services/env-test");
        Program._config = BuildConfig("""
        {
            "UseSlack": true,
            "SlackReportInterval": 7200,
            "SiteChecks": [{ "url": "https://example.com", "interval": 60 }]
        }
        """);
        Program.ValidateConfig();
        Assert.True(Program._useSlack);
        Assert.Equal(7200, Program._slackReportInterval);
    }

    [Fact]
    public void validates_interval_minimum()
    {
        Program._config = BuildConfig("""
        {
            "SiteChecks": [{ "url": "https://example.com", "interval": 1 }]
        }
        """);
        Assert.False(Program.ValidateConfig());
    }
}
