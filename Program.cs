using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Configuration;
using System.Diagnostics.CodeAnalysis;
using LiteDB;
using System.Threading.Tasks;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Peek
{
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    internal class Program
    {
        private static IConfigurationRoot configuration;
        private static string message = String.Empty;
        private static bool runOnce;
        private static bool useSlack;
        public static int slackReportInterval;

        // Reuse a single HttpClient with custom SSL validation to improve performance and avoid socket exhaustion
        private static readonly HttpClientHandler handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = CustomCallback };
        private static readonly HttpClient client = new HttpClient(handler);

        static async Task Main(string[] args)
        {
            // Load configuration from appsettings.json
            var configurationBuilder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json");
            configuration = configurationBuilder.Build();

            // Read configuration values safely
            Boolean.TryParse(configuration["RunOnce"], out runOnce);
            Boolean.TryParse(configuration["UseSlack"], out useSlack);
            Int32.TryParse(configuration["SlackReportInterval"], out slackReportInterval);
            if (slackReportInterval == 0) slackReportInterval = 14400; // Default to 12 hours if missing or invalid

            // Initialize NoSQL database connection and retrieve all current records (if any). Create db if it doesn't exist.
            using (var db = new LiteDatabase("filename=Peek.db; mode=Exclusive"))
            {
                var dbCollection = db.GetCollection<SiteCheck>("SiteCheckStates");
                SyncConfigWithDatabase(configuration, dbCollection); // Ensure database reflects latest config
                await ProcessSiteChecksAsync(dbCollection); // Begin the checking loop
            }
        }

        private static void SyncConfigWithDatabase(IConfigurationRoot config, ILiteCollection<SiteCheck> collection)
        {
            // Sync each configured SiteCheck from appsettings.json into the local DB, updating existing records or inserting new ones
            var configValues = config.GetSection("SiteChecks");
            foreach (var s in configValues.GetChildren())
            {
                var url = s.GetValue<string>("url")?.Sanitize();
                if (String.IsNullOrWhiteSpace(url)) continue; // Skip invalid or empty URLs
                var interval = s.GetValue<int>("interval");
                var searchString = s.GetValue<string>("searchString");

                var exists = collection.Exists(Query.EQ("URL", url));

                // Update existing record
                if (exists)
                {
                    var currentRecord = collection.FindOne(Query.EQ("URL", url));
                    currentRecord.Interval = interval;
                    currentRecord.SearchString = searchString;
                    currentRecord.ConfigUpdated = DateTime.Now;
                    collection.Update(currentRecord);
                }
                // Otherwise, insert new record
                else
                {
                    var newRecord = new SiteCheck
                    {
                        URL = url,
                        Interval = interval,
                        SearchString = searchString,
                        LastState = 200,
                        Message = String.Empty,
                        NextCheck = DateTime.Now,
                        ConfigUpdated = DateTime.Now,
                        NextNotification = DateTime.Now
                    };
                    collection.Insert(newRecord);
                }
            }

            // Delete DB records that are no longer present in the config file.
            // We use the ConfigUpdated timestamp as a marker. Everything still in the config was just updated.
            var staleRecords = collection.Find(r => r.ConfigUpdated < DateTime.Now.AddMinutes(-5));
            foreach (var sr in staleRecords)
            {
                collection.Delete(sr.Id);
            }

            // Ensure there's an index on URL for fast lookups
            collection.EnsureIndex(r => r.URL, true);
        }

        [SuppressMessage("ReSharper", "PossibleMultipleEnumeration")]
        private static async Task ProcessSiteChecksAsync(ILiteCollection<SiteCheck> collection)
        {
            // Load current site check states from DB
            var siteChecks = collection.Find(Query.All()).ToList();

            while (true)
            {
                foreach (var currentCheck in siteChecks)
                {
                    if (currentCheck.NextCheck < DateTime.Now)
                    {
                        HttpResponseMessage response = null;
                        var responseContent = String.Empty;
                        var statusCode = 410; // 'Gone' is used as default failure code
                        message = String.Empty;

                        // Make a GET request to the site with cert validation
                        try
                        {
                            response = await client.GetAsync(currentCheck.URL);
                        }
                        catch (Exception ex)
                        {
                            message += $", {ex.Message}"; // Append exception message if request fails
                        }

                        // Attempt to read response content if available
                        if (response != null)
                        {
                            statusCode = (int)response.StatusCode;
                            try
                            {
                                responseContent = await response.Content.ReadAsStringAsync();
                            }
                            catch (Exception ex)
                            {
                                message += $", {ex.Message}";
                            }
                        }

                        // Determine status based on content search
                        if (currentCheck.SearchString != "*" && response != null && !responseContent.Contains(currentCheck.SearchString))
                        {
                            // If the expected content is missing, use negative HTTP status code (eg. "-200")
                            currentCheck.LastState = statusCode - (2 * statusCode);
                        }
                        else
                        {
                            currentCheck.LastState = statusCode;
                        }
                        currentCheck.Message = message.Sanitize();

                        // Prevent NextCheck from being pushed too far into the future as a result of repeated fast loops.
                        if (currentCheck.NextCheck < DateTime.Now)
                        {
                            currentCheck.NextCheck = currentCheck.NextCheck.AddSeconds(currentCheck.Interval);
                        }

                        var storedRecord = collection.FindOne(Query.EQ("URL", currentCheck.URL));

                        // If Slack is enabled, notify on relevant state changes
                        if (useSlack)
                        {
                            await SendSlackNotificationAsync(currentCheck.URL, storedRecord.LastState, currentCheck.LastState, currentCheck.NextNotification, currentCheck.Message);
                            currentCheck.NextNotification = currentCheck.NextNotification.Update();
                        }

                        // Update DB with the new state
                        storedRecord.LastState = currentCheck.LastState;
                        storedRecord.Message = currentCheck.Message;
                        storedRecord.NextCheck = currentCheck.NextCheck;
                        storedRecord.NextNotification = currentCheck.NextNotification;
                        collection.Update(storedRecord);

                        // Log the outcome to console
                        Console.WriteLine($"{DateTime.Now.FormattedDate()}, {currentCheck.URL}, {Math.Abs(currentCheck.LastState)}, {currentCheck.LastState.ToStatusCodeText()} {currentCheck.Message}");
                    }
                }

                // If RunOnce is true, break out after one loop
                if (runOnce) break;

                // Reorder checks by NextCheck so more frequent checks aren't delayed
                siteChecks = siteChecks.OrderBy(s => s.NextCheck).ToList();
            }
        }

        private static bool CustomCallback(HttpRequestMessage req, X509Certificate2 cert, X509Chain chain, SslPolicyErrors errors)
        {
            // Custom certificate validation: warn if cert expires within the next 30 days
            var threshold = DateTime.UtcNow.AddDays(30);
            var certExpiry = Convert.ToDateTime(cert.GetExpirationDateString());

            if (certExpiry < threshold)
            {
                var daysToGo = (certExpiry - DateTime.UtcNow).Days;
                message += $", Certificate is expiring in {daysToGo} days at {certExpiry.FormattedDate()} UTC";
            }

            // Proceed only if there are no SSL policy errors
            return errors == SslPolicyErrors.None;
        }

        private static async Task SendSlackNotificationAsync(string url, int previousState, int currentState, DateTime nextNotification, string message)
        {
            if (String.IsNullOrEmpty(configuration["SlackWebHookURL"])) return; // Skip if no webhook is configured

            string status = null;
            string colour = null;

            // Determine the nature of the change
            if ((Math.Abs(previousState) < 300 && Math.Abs(currentState) > 400) || (previousState > 0 && currentState < 0 && Math.Abs(currentState) < 300))
            {
                status = "down"; // Site went down
                colour = "danger";
            }
            else if ((Math.Abs(previousState) > 400 && Math.Abs(currentState) < 300) || (previousState < 0 && currentState > 0 && currentState < 300))
            {
                status = "recovered"; // Site recovered
                colour = "good";
            }
            else if (Math.Abs(previousState) < 300 && Math.Abs(currentState) < 300 && !String.IsNullOrEmpty(message) && DateTime.Now > nextNotification)
            {
                status = "information"; // Info message without a state change
                colour = "warning";
            }
            else if (previousState != currentState)
            {
                status = "status change"; // Some other status variation
                colour = "warning";
            }

            if (String.IsNullOrEmpty(status)) return;

            message = Environment.NewLine + message.Sanitize();
            var messageBody = $"Last known state: HTTP {Math.Abs(previousState)}, {previousState.ToStatusCodeText()}\nCurrent state: HTTP {Math.Abs(currentState)}, {currentState.ToStatusCodeText()}{message}";

            // Build structured JSON payload for Slack
            var payload = new
            {
                channel = "#notifications",
                username = "Peek",
                text = $"*{status.ToUpper()}*",
                attachments = new[]
                {
                    new
                    {
                        color = colour,
                        fields = new[]
                        {
                            new { title = url, value = messageBody }
                        }
                    }
                }
            };

            try
            {
                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                await client.PostAsync(configuration["SlackWebHookURL"], content);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unable to post notification to Slack: {ex.Message}");
            }
        }
    }
}
