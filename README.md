# Peek

Peek is a simple, self-hosted uptime monitoring tool inspired by updown.io. It periodically checks websites you configure, verifies expected content, logs status, and optionally sends notifications to Slack when a site status changes. Ideal for devs, ops, devops, or SREs who look after several websites and need a lightweight website monitoring tool.

---

## Features

- Monitor multiple websites on custom intervals
- Validate page content with optional search strings
- Detect HTTPS certificate expiry early
- Send rich Slack notifications on status changes
- Persist site states locally using a lightweight embedded NoSQL database (LiteDB)
- Site checks are configurable via a simple JSON file (appsettings.json)
- Flexible operation mode: run continuously or as a one-off check

---

## Getting Started

1. Install .NET 9 
2. Clone or download the repo
3. Configure your monitoring targets and settings in `appsettings.json`
4. Run Peek either as a continuous monitoring script/service or as a one-off health check

---

## Configuration Overview

| Setting             | Description                                                                                                                                    |
|---------------------|------------------------------------------------------------------------------------------------------------------------------------------------|
| `RunOnce`           | Determines whether Peek runs once and exits (`true`) or runs continuously in a loop (`false`). Useful for cron jobs or one-time health checks. |
| `UseSlack`          | Enables or disables Slack notifications. Set to `true` to receive alerts in Slack.                                                             |
| `SlackWebHookURL`   | The Slack Incoming Webhook URL to which notifications will be sent. Required if `UseSlack` is `true`.                                          |
| `SlackReportInterval` | Minimum number of seconds between Slack notifications to prevent spamming when a site remains down. Default is 14400 seconds (4 hours).        |
| `SiteChecks`        | A list of site check configurations. Each entry must include:                                                                                  |
|                     | - `url`: the website to monitor                                                                                                                |
|                     | - `interval`: how often to check (in seconds)                                                                                                  |
|                     | - `searchString`: text or wildcard `*` to look for in the HTTP response                                                                        |

---

## Example `appsettings.json`

```json
{
  "RunOnce": false,
  "UseSlack": true,
  "SlackWebHookURL": "https://hooks.slack.com/services/XXX/YYY/ZZZ",
  "SlackReportInterval": 14400,
  "SiteChecks": [
    {
      "url": "https://example.com",
      "interval": 60,
      "searchString": "Welcome"
    },
    {
      "url": "https://another-site.com",
      "interval": 300,
      "searchString": "*"
    }
  ]
}
```

---

## Run modes

### Continuous Mode (default)

Peek runs an infinite loop, checking sites at their specified intervals and sending notifications as needed.

### Single-Run Mode

Set "RunOnce": true to make Peek check all sites once and then exit.

This mode is perfect for:
- Post-deployment health checks: Run Peek once after a release to catch issues early. 
- Solutions where scheduling is done via OS scheduler (cron, Task Scheduler, etc).

---

## Feedback and Contributions

Got ideas or improvements? Feel free to open issues or pull requests!