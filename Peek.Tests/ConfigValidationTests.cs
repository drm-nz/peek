using System.Text;
using Microsoft.Extensions.Configuration;

namespace Peek.Tests;

public class ConfigValidationTests
{
    private static IConfigurationRoot ConfigFromJson(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return new ConfigurationBuilder().AddJsonStream(stream).Build();
    }

    [Fact]
    public void valid_config_returns_no_errors()
    {
        var config = ConfigFromJson("""
        {
            "UseSlack": false,
            "SlackReportInterval": 14400,
            "SiteChecks": [
                { "url": "https://example.com", "interval": 60, "searchString": "*" }
            ]
        }
        """);

        var errors = Program.GetConfigErrors(config, null, out var useSlack, out var interval);

        Assert.Empty(errors);
        Assert.False(useSlack);
        Assert.Equal(14400, interval);
    }

    [Fact]
    public void empty_SiteChecks_returns_error()
    {
        var config = ConfigFromJson("""
        {
            "UseSlack": false,
            "SiteChecks": []
        }
        """);

        var errors = Program.GetConfigErrors(config, null, out _, out _);

        Assert.Contains(errors, e => e.Contains("No SiteChecks"));
    }

    [Fact]
    public void missing_url_returns_error()
    {
        var config = ConfigFromJson("""
        {
            "SiteChecks": [
                { "interval": 60 }
            ]
        }
        """);

        var errors = Program.GetConfigErrors(config, null, out _, out _);

        Assert.Contains(errors, e => e.Contains("missing or empty url"));
    }

    [Fact]
    public void empty_url_returns_error()
    {
        var config = ConfigFromJson("""
        {
            "SiteChecks": [
                { "url": "", "interval": 60 }
            ]
        }
        """);

        var errors = Program.GetConfigErrors(config, null, out _, out _);

        Assert.Contains(errors, e => e.Contains("missing or empty url"));
    }

    [Fact]
    public void invalid_url_returns_error()
    {
        var config = ConfigFromJson("""
        {
            "SiteChecks": [
                { "url": "not-a-url", "interval": 60 }
            ]
        }
        """);

        var errors = Program.GetConfigErrors(config, null, out _, out _);

        Assert.Contains(errors, e => e.Contains("Invalid URL"));
    }

    [Fact]
    public void unsupported_scheme_returns_error()
    {
        var config = ConfigFromJson("""
        {
            "SiteChecks": [
                { "url": "ftp://example.com", "interval": 60 }
            ]
        }
        """);

        var errors = Program.GetConfigErrors(config, null, out _, out _);

        Assert.Contains(errors, e => e.Contains("Invalid URL"));
    }

    [Fact]
    public void interval_below_minimum_returns_error()
    {
        var config = ConfigFromJson("""
        {
            "SiteChecks": [
                { "url": "https://example.com", "interval": 1 }
            ]
        }
        """);

        var errors = Program.GetConfigErrors(config, null, out _, out _);

        Assert.Contains(errors, e => e.Contains("interval < 5"));
    }

    [Fact]
    public void interval_at_minimum_is_valid()
    {
        var config = ConfigFromJson("""
        {
            "SiteChecks": [
                { "url": "https://example.com", "interval": 5 }
            ]
        }
        """);

        var errors = Program.GetConfigErrors(config, null, out _, out _);

        Assert.DoesNotContain(errors, e => e.Contains("interval"));
    }

    [Fact]
    public void useSlack_no_webhook_returns_error()
    {
        var config = ConfigFromJson("""
        {
            "UseSlack": true,
            "SiteChecks": [
                { "url": "https://example.com", "interval": 60 }
            ]
        }
        """);

        var errors = Program.GetConfigErrors(config, null, out var useSlack, out _);

        Assert.True(useSlack);
        Assert.Contains(errors, e => e.Contains("webhook URL"));
    }

    [Fact]
    public void useSlack_with_config_webhook_is_valid()
    {
        var config = ConfigFromJson("""
        {
            "UseSlack": true,
            "SlackWebHookURL": "https://hooks.slack.com/services/xxx",
            "SiteChecks": [
                { "url": "https://example.com", "interval": 60 }
            ]
        }
        """);

        var errors = Program.GetConfigErrors(config, null, out _, out _);

        Assert.DoesNotContain(errors, e => e.Contains("webhook URL"));
    }

    [Fact]
    public void useSlack_with_env_webhook_is_valid()
    {
        var config = ConfigFromJson("""
        {
            "UseSlack": true,
            "SiteChecks": [
                { "url": "https://example.com", "interval": 60 }
            ]
        }
        """);

        var errors = Program.GetConfigErrors(config, "https://hooks.slack.com/services/env", out _, out _);

        Assert.DoesNotContain(errors, e => e.Contains("webhook URL"));
    }

    [Fact]
    public void slackReportInterval_defaults_when_zero()
    {
        var config = ConfigFromJson("""
        {
            "SlackReportInterval": 0,
            "SiteChecks": [
                { "url": "https://example.com", "interval": 60 }
            ]
        }
        """);

        Program.GetConfigErrors(config, null, out _, out var interval);

        Assert.Equal(14400, interval);
    }

    [Fact]
    public void slackReportInterval_defaults_when_negative()
    {
        var config = ConfigFromJson("""
        {
            "SlackReportInterval": -1,
            "SiteChecks": [
                { "url": "https://example.com", "interval": 60 }
            ]
        }
        """);

        Program.GetConfigErrors(config, null, out _, out var interval);

        Assert.Equal(14400, interval);
    }

    [Fact]
    public void multiple_errors_are_returned()
    {
        var config = ConfigFromJson("""
        {
            "UseSlack": true,
            "SiteChecks": [
                { "url": "", "interval": 1 }
            ]
        }
        """);

        var errors = Program.GetConfigErrors(config, null, out _, out _);

        Assert.Contains(errors, e => e.Contains("missing or empty url"));
        Assert.Contains(errors, e => e.Contains("webhook URL"));
    }
}
