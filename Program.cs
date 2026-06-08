using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Security;
using System.Runtime.CompilerServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LiteDB;
using Microsoft.Extensions.Configuration;
using JsonSerializer = System.Text.Json.JsonSerializer;

[assembly: InternalsVisibleTo("Peek.Tests")]

namespace Peek;

internal class Program
{
    private static readonly HttpClientHandler Handler = new()
    {
        ServerCertificateCustomValidationCallback = CertificateCallback
    };

    private static readonly HttpClient Client = new(Handler);

    private static IConfigurationRoot _config = null!;
    private static string _slackWebhook = string.Empty;
    private static string _slackChannel = "#notifications";
    private static int _slackReportInterval = 14400;
    private static bool _useSlack;

    static async Task<int> Main()
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", false, false);
        _config = builder.Build();

        Log.Initialise();

        if (!ValidateConfig())
            return 1;

        _slackWebhook = Environment.GetEnvironmentVariable("PEEK_SLACK_WEBHOOK_URL")
                        ?? _config["SlackWebHookURL"]
                        ?? string.Empty;
        _slackChannel = _config["SlackChannel"] ?? "#notifications";

        var httpTimeout = _config.GetValue<int?>("HttpTimeoutSeconds") ?? 15;
        if (httpTimeout is < 1 or > 120) httpTimeout = 15;
        Client.Timeout = TimeSpan.FromSeconds(httpTimeout);

        var dbPath = _config["DbPath"] ?? "Peek.db";
        using var db = new LiteDatabase($"filename={dbPath}; mode=Exclusive");
        var collection = db.GetCollection<SiteCheck>("SiteCheckStates");

        SyncConfigWithDatabase(collection);

        var (total, failed) = await ProcessSiteChecksAsync(collection);

        Log.Info(failed == 0
            ? $"Peek finished: {total} checked, all OK"
            : $"Peek finished: {total} checked, {failed} failed");

        return failed > 0 ? 1 : 0;
    }

    private static bool ValidateConfig()
    {
        var envWebhook = Environment.GetEnvironmentVariable("PEEK_SLACK_WEBHOOK_URL");
        var errors = GetConfigErrors(_config, envWebhook, out _useSlack, out _slackReportInterval);

        if (errors.Count == 0) return true;

        foreach (var err in errors)
            Log.Error($"Config: {err}");
        return false;
    }

    internal static List<string> GetConfigErrors(
        IConfigurationRoot config, string? slackWebhookFromEnv,
        out bool useSlack, out int slackReportInterval)
    {
        var errors = new List<string>();

        useSlack = config.GetValue<bool>("UseSlack");
        slackReportInterval = config.GetValue<int>("SlackReportInterval");
        if (slackReportInterval <= 0) slackReportInterval = 14400;

        if (useSlack)
        {
            var configWebhook = config["SlackWebHookURL"];
            if (string.IsNullOrWhiteSpace(slackWebhookFromEnv) && string.IsNullOrWhiteSpace(configWebhook))
                errors.Add("UseSlack is true but no webhook URL configured. Set SlackWebHookURL in config or PEEK_SLACK_WEBHOOK_URL env var.");
        }

        var sites = config.GetSection("SiteChecks").GetChildren().ToList();
        if (sites.Count == 0)
            errors.Add("No SiteChecks configured");

        foreach (var site in sites)
        {
            var url = site["url"];
            if (string.IsNullOrWhiteSpace(url))
            {
                errors.Add("A SiteCheck entry has a missing or empty url");
                continue;
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != "http" && uri.Scheme != "https"))
                errors.Add($"Invalid URL in SiteChecks: '{url}'");

            if (site.GetValue<int>("interval") < 5)
                errors.Add($"Site '{url}' has interval < 5s");
        }

        return errors;
    }

    private static void SyncConfigWithDatabase(ILiteCollection<SiteCheck> collection)
    {
        foreach (var s in _config.GetSection("SiteChecks").GetChildren())
        {
            var url = s["url"]?.Sanitize();
            if (string.IsNullOrWhiteSpace(url)) continue;

            var interval = s.GetValue<int>("interval");
            var searchString = s.GetValue<string>("searchString");

            if (collection.Exists(Query.EQ("URL", url)))
            {
                var record = collection.FindOne(Query.EQ("URL", url));
                record.Interval = interval;
                record.SearchString = searchString ?? string.Empty;
                record.ConfigUpdated = DateTime.Now;
                collection.Update(record);
            }
            else
            {
                collection.Insert(new SiteCheck
                {
                    URL = url,
                    Interval = interval,
                    SearchString = searchString ?? string.Empty,
                    LastState = 200,
                    Message = string.Empty,
                    NextCheck = DateTime.Now,
                    ConfigUpdated = DateTime.Now,
                    NextNotification = DateTime.Now
                });
            }
        }

        var stale = collection.Find(r => r.ConfigUpdated < DateTime.Now.AddMinutes(-5));
        foreach (var s in stale)
            collection.Delete(s.Id);

        collection.EnsureIndex(r => r.URL, true);
    }

    private static async Task<(int Total, int Failed)> ProcessSiteChecksAsync(
        ILiteCollection<SiteCheck> collection)
    {
        var sites = collection.FindAll().ToList();
        var due = sites.Where(s => s.NextCheck <= DateTime.Now).ToList();

        if (due.Count == 0)
            return (0, 0);

        Log.Info($"Checking {due.Count} site(s)...");

        var maxConcurrency = _config.GetValue<int>("MaxConcurrency");
        if (maxConcurrency < 1) maxConcurrency = 1;

        if (maxConcurrency > 1)
        {
            var semaphore = new SemaphoreSlim(maxConcurrency);
            var tasks = due.Select(async site =>
            {
                await semaphore.WaitAsync();
                try { await CheckSiteAsync(site); }
                finally { semaphore.Release(); }
            });
            await Task.WhenAll(tasks);
        }
        else
        {
            foreach (var site in due)
                await CheckSiteAsync(site);
        }

        var failed = 0;
        foreach (var site in due)
        {
            var isFailure = Math.Abs(site.LastState) >= 400;
            if (isFailure) failed++;

            if (_useSlack && !string.IsNullOrEmpty(_slackWebhook))
            {
                var stored = collection.FindOne(Query.EQ("URL", site.URL));
                await SendSlackNotificationAsync(
                    site.URL, stored.LastState, site.LastState,
                    site.NextNotification, site.Message);
            }

            site.NextNotification = DateTime.Now > site.NextNotification
                ? site.NextNotification.AddSeconds(_slackReportInterval)
                : site.NextNotification;

            var record = collection.FindOne(Query.EQ("URL", site.URL));
            record.LastState = site.LastState;
            record.Message = site.Message;
            record.NextCheck = site.NextCheck;
            record.NextNotification = site.NextNotification;
            collection.Update(record);
        }

        return (due.Count, failed);
    }

    private static async Task CheckSiteAsync(SiteCheck site)
    {
        var messages = new List<string>();
        HttpResponseMessage? response = null;
        var statusCode = 410;
        var sw = Stopwatch.StartNew();

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                response = await Client.GetAsync(site.URL);
                sw.Stop();
                break;
            }
            catch (Exception ex)
            {
                sw.Stop();
                messages.Add(ex.Message);
                if (attempt == 1)
                {
                    Log.Warn($"Retrying {site.URL}: {ex.Message}");
                    await Task.Delay(5_000);
                    sw.Restart();
                }
            }
        }

        string? responseContent = null;
        if (response != null)
        {
            statusCode = (int)response.StatusCode;
            try
            {
                responseContent = await response.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                messages.Add(ex.Message);
            }
        }

        site.LastState = DetermineStatusWithContentCheck(statusCode, responseContent, site.SearchString);

        site.Message = string.Join(", ", messages).Sanitize() ?? string.Empty;
        site.NextCheck = DateTime.Now.AddSeconds(site.Interval);

        var isFailure = Math.Abs(site.LastState) >= 400 || response == null;
        var logLine =
            $"{site.URL}, {Math.Abs(site.LastState)}, {site.LastState.ToStatusCodeText()}, {sw.ElapsedMilliseconds}ms";

        if (isFailure)
            Log.Error(logLine);
        else
            Log.Info(logLine);
    }

    private static bool CertificateCallback(
        HttpRequestMessage req, X509Certificate2? cert, X509Chain? chain, SslPolicyErrors errors)
    {
        try
        {
            if (cert != null)
            {
                var threshold = DateTime.UtcNow.AddDays(30);
                var expiry = Convert.ToDateTime(cert.GetExpirationDateString());
                if (expiry < threshold)
                {
                    var days = (expiry - DateTime.UtcNow).Days;
                    Log.Warn(
                        $"Certificate for {req.RequestUri?.Host} expires in {days} days " +
                        $"on {expiry:yyyy-MM-dd HH:mm:ss} UTC");
                }
            }
        }
        catch
        {
            // Don't fail the check if cert inspection throws
        }

        return errors == SslPolicyErrors.None;
    }

    private static async Task SendSlackNotificationAsync(
        string url, int previousState, int currentState,
        DateTime nextNotification, string message)
    {
        if (string.IsNullOrEmpty(_slackWebhook)) return;

        var (status, colour) = DetermineSlackStatus(
            previousState, currentState, message, nextNotification, DateTime.Now);
        if (status == null) return;

        var slackMsg = $"\n{message.Sanitize()}";
        var text =
            $"Last state: HTTP {Math.Abs(previousState)}, {previousState.ToStatusCodeText()}\n" +
            $"Current state: HTTP {Math.Abs(currentState)}, {currentState.ToStatusCodeText()}{slackMsg}";

        var payload = new
        {
            channel = _slackChannel,
            username = "Peek",
            text = $"*{status.ToUpper()}*",
            attachments = new[]
            {
                new
                {
                    color = colour,
                    fields = new[]
                    {
                        new { title = url, value = text }
                    }
                }
            }
        };

        try
        {
            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            await Client.PostAsync(_slackWebhook, content);
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to send Slack notification: {ex.Message}");
        }
    }

    internal static (string? Status, string? Colour) DetermineSlackStatus(
        int previousState, int currentState, string message,
        DateTime nextNotification, DateTime now)
    {
        var absPrev = Math.Abs(previousState);
        var absCurr = Math.Abs(currentState);

        if ((absPrev < 300 && absCurr > 400) ||
            (previousState > 0 && currentState < 0 && absCurr < 300))
            return ("down", "danger");

        if ((absPrev > 400 && absCurr < 300) ||
            (previousState < 0 && currentState > 0 && absCurr < 300))
            return ("recovered", "good");

        if (absPrev < 300 && absCurr < 300 &&
            !string.IsNullOrEmpty(message) && now > nextNotification)
            return ("information", "warning");

        if (previousState != currentState)
            return ("status change", "warning");

        return (null, null);
    }

    internal static int DetermineStatusWithContentCheck(
        int httpStatus, string? responseContent, string? searchString)
    {
        if (searchString != null && searchString != "*" &&
            responseContent != null && !responseContent.Contains(searchString))
            return -httpStatus;
        return httpStatus;
    }
}
