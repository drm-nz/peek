using System;
using System.IO;
using System.Net;
using System.Text;
using System.Linq;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;
using LiteDB;

namespace Peek
{
    class Program
    {
        private static IConfigurationRoot configuration;
        private static string message = string.Empty;
        private static bool runOnce;
        private static bool useSlack;
        public static int slackReportInterval;

        static void Main(string[] args)
        {
            // Configuration
            IConfigurationBuilder configurationBuilder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json");
            configuration = configurationBuilder.Build();
            
            runOnce = String.IsNullOrEmpty(configuration["RunOnce"]) ? false : Convert.ToBoolean(configuration["RunOnce"]);
            useSlack = String.IsNullOrEmpty(configuration["UseSlack"]) ? false : Convert.ToBoolean(configuration["UseSlack"]);
            slackReportInterval = String.IsNullOrEmpty(configuration["SlackReportInterval"]) ? 14400 : Convert.ToInt32(configuration["SlackReportInterval"]); // Default to 12 hours
            
            // Initialize NoSQL db connection and retreive all current records (if any). Create db if doesn't exist.
            LiteCollection<SiteCheck> dbCollection;
            using (LiteDatabase db = new LiteDatabase(configuration["DbConnectionString"]))
            {
                dbCollection = db.GetCollection<SiteCheck>("SiteCheckStates");
            }

            SyncConfigWithDatabase(configuration, dbCollection);
            ProcessSiteChecks(dbCollection);
        }

        private static void SyncConfigWithDatabase(IConfigurationRoot config, LiteCollection<SiteCheck> collection)
        {
            // Get list of current site checks from appsettings.json
            IConfigurationSection configValues = config.GetSection("SiteChecks");
            foreach (var s in configValues.GetChildren())
            {
                string url = s.GetValue<string>("url").Sanitize();
                int interval = s.GetValue<int>("interval");
                string searchString = s.GetValue<string>("searchString");

                // Check if there is already a record in the db with this URL
                SiteCheck currentRecord = null;
                bool exists = collection.Exists(Query.EQ("URL", url));

                // If there is, update the record
                if (exists)
                {
                    currentRecord = collection.Find(Query.EQ("URL", url)).First();
                    currentRecord.Interval = interval;
                    currentRecord.SearchString = searchString;
                    currentRecord.ConfigUpdated = DateTime.Now;
                    _ = collection.Update(currentRecord);
                }
                // Otherwise insert a new record for it
                else
                {
                    SiteCheck newRecord = new SiteCheck
                    {
                        URL = url,
                        Interval = interval,
                        SearchString = searchString,
                        LastState = 200,
                        Message = string.Empty,
                        NextCheck = DateTime.Now,
                        ConfigUpdated = DateTime.Now,
                        NextNotification = DateTime.Now
                    };
                    _ = collection.Insert(newRecord);
                }
            }
            // Delete db records that are not present in the config file anymore. We can tell this by looking a SiteCheck.ConfigUpdated
            // because every record that's in appsettings.json will have been updated/inserted at this point. 
            IEnumerable<SiteCheck> staleRecords = collection.Find(r => r.ConfigUpdated < DateTime.Now.AddMinutes(-15));
            foreach (SiteCheck sr in staleRecords)
            {
                collection.Delete(sr.Id);
            }

            // And finally make sure the database is indexed by URL. If there is an index on URL already, this line does nothing.
            collection.EnsureIndex(r => r.URL, true);
        }

        private static void ProcessSiteChecks(LiteCollection<SiteCheck> collection)
        {
            // Query records into an IEnumerable
            IEnumerable<SiteCheck> SiteChecks = collection.Find(Query.All());
            while (true)
            {
                foreach (SiteCheck currentCheck in SiteChecks)
                {
                    if (currentCheck.NextCheck < DateTime.Now)
                    {
                        HttpResponseMessage response = null;
                        string responseContent = string.Empty;
                        int statusCode = 410; // Use 'Gone' for starters
                        message = string.Empty;

                        // Make a request to the site
                        HttpClientHandler handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = CustomCallback };
                        using (HttpClient client = new HttpClient(handler))
                        {
                            try
                            {
                                response = client.GetAsync(currentCheck.URL).GetAwaiter().GetResult();
                            }
                            catch (Exception ex)
                            {
                                message += $", {ex.Message}";
                            }
                        }

                        if (response != null)
                        {
                            statusCode = (int)response.StatusCode;

                            // Get the actual content of HttpResponseMessage
                            try
                            {
                                responseContent = response.Content.ReadAsStringAsync().Result;
                            }
                            catch (Exception ex)
                            {
                                message += $", {ex.Message}";
                            }
                        }

                        // If we are looking for a particular content and it's not there, use negative HTTP status code (eg. "-200")
                        if (currentCheck.SearchString != "*" && response != null && !responseContent.Contains(currentCheck.SearchString))
                        {
                            currentCheck.LastState = statusCode - (2 * statusCode);
                        }
                        // Otherwise if we are not looking for any particular content, or we do and it's there, use normal HTTP code (eg "200")
                        else
                        {
                            currentCheck.LastState = statusCode;
                        }
                        currentCheck.Message = message.Sanitize();

                        // Only update currentCheck.NextCheck if it's in the past. This is to guard agains quick subsequent runs/restarts
                        // shooting the next check into the distant future.
                        currentCheck.NextCheck = currentCheck.NextCheck < DateTime.Now ? currentCheck.NextCheck.AddSeconds(currentCheck.Interval) : currentCheck.NextCheck;

                        // Get matching database record to compare
                        SiteCheck storedRecord = collection.Find(Query.EQ("URL", currentCheck.URL)).First();
                                                
                        if (useSlack)
                        {
                            SendSlackNotification(currentCheck.URL, storedRecord.LastState, currentCheck.LastState, currentCheck.NextNotification, currentCheck.Message);
                            currentCheck.NextNotification = currentCheck.NextNotification.Update();
                        }

                        // Update site state in the database
                        storedRecord.LastState = currentCheck.LastState;
                        storedRecord.Message = currentCheck.Message;
                        storedRecord.NextCheck = currentCheck.NextCheck;
                        storedRecord.NextNotification = currentCheck.NextNotification;
                        _ = collection.Update(storedRecord);

                        // Print current results to Console/Log
                        Console.WriteLine($"{DateTime.Now.FormattedDate()}, {currentCheck.URL}, {Math.Abs(currentCheck.LastState)}, {currentCheck.LastState.ToStatusCodeText()} {currentCheck.Message}");
                    }
                }

                // If RunOnce is set to true, jump out of the loop
                if (runOnce) { break; }

                // Once we're finished with processing and updating the list, reorder based on NextCheck. This is necessary because
                // some sites might have more frequent checks than others. If, say, a record has a 5 second check interval but the list
                // is so long that it takes 10 seconds to go through it, we may not be able to check the site as often as required.
                SiteChecks = SiteChecks.OrderBy(s => s.NextCheck).ToList();
            }
        }

        private static bool CustomCallback(HttpRequestMessage arg1, X509Certificate2 arg2, X509Chain arg3, SslPolicyErrors arg4)
        {
            DateTime _30DaysAhead = DateTime.UtcNow.AddDays(30);
            DateTime certExpiry = Convert.ToDateTime(arg2.GetExpirationDateString());            

            if (certExpiry < _30DaysAhead)
            {
                int daysToGo = (certExpiry - DateTime.UtcNow).Days;
                message += $", Certificate is expiring in {daysToGo} days at {certExpiry.FormattedDate()} UTC";                
            }
            return arg4 == SslPolicyErrors.None;
        }

        public static void SendSlackNotification(string url, int previousState, int currentState, DateTime nextNotification, string message)            
        {           
            if (!String.IsNullOrEmpty(configuration["SlackWebHookURL"]))
            {                
                string status = String.Empty;
                string colour = String.Empty;

                try
                {
                    WebRequest request = WebRequest.Create(configuration["SlackWebHookURL"]);
                    request.Method = "POST";
               
                    // If it was ok previously but it isn't now or we've lost the correct content (down)
                    if ((Math.Abs(previousState) < 300 && Math.Abs(currentState) > 400) || (previousState > 0 && currentState < 0 && Math.Abs(currentState) < 300))
                    {
                        status = "down";
                        colour = "danger";                        
                    }
                    // If it wasn't ok previously but it is now or recovered from incorrect content (recovered)
                    else if ((Math.Abs(previousState) > 400 && Math.Abs(currentState) < 300) || (previousState < 0 && currentState > 0 && currentState < 300))
                    {
                        status = "recovered";
                        colour = "good";                        
                    }                 
                    // If it was ok and still is, but there is an important message (information). Don't report it every time.
                    else if (Math.Abs(previousState) < 300 && Math.Abs(currentState) < 300 && !String.IsNullOrEmpty(message) && DateTime.Now > nextNotification)
                    {
                        status = "information";
                        colour = "warning";                        
                    }
                    // If there is a change, but we're unsure how important it is (status change)
                    else if (previousState != currentState)
                    {
                        status = "status change";
                        colour = "warning";
                    }

                    if (!String.IsNullOrEmpty(status)) { 
                        message = Environment.NewLine + message.Sanitize();
                        string messageBody = $"Last known state: HTTP {Math.Abs(previousState)}, {previousState.ToStatusCodeText()}\nCurrent state: HTTP {Math.Abs(currentState)}, {currentState.ToStatusCodeText()}{message}";
                        string payload = $"payload={{'channel': '#notifications', 'username': 'Peek','text':'*{status.ToUpper()}*', 'attachments': [{{ 'color':'{colour}', 'fields': [{{ 'title':'{url}', 'value': '{messageBody}' }}] }}] }}";
                        byte[] byteArray = Encoding.UTF8.GetBytes(payload);
                        request.ContentType = "application/x-www-form-urlencoded";
                        request.ContentLength = byteArray.Length;
                        Stream dataStream = request.GetRequestStream();
                        dataStream.Write(byteArray, 0, byteArray.Length);
                        _ = request.GetResponse();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Unable to post notification to Slack: {ex.Message}");
                }
            }
        }
    }
}
