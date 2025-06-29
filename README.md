# Peek

A simple self-hosted updown.io alternative for monitoring website availability with optional Slack integration. Ideal for devs, ops, or SREs responsible for managing many websites who need a lightweight, reliable website monitoring tool without the price tag.

---

## Configuration Overview

| Setting              | Description                                                                                                                                    |
|----------------------|------------------------------------------------------------------------------------------------------------------------------------------------|
| `RunOnce`            | Determines whether Peek runs once and exits (`true`) or runs continuously in a loop (`false`). Useful for cron jobs or one-time health checks. |
| `UseSlack`           | Enables or disables Slack notifications. Set to `true` to receive alerts in Slack.                                                             |
| `SlackWebHookURL`    | The Slack Incoming Webhook URL to which notifications will be sent. Required if `UseSlack` is `true`.                                          |
| `SlackReportInterval`| Minimum number of seconds between Slack notifications to prevent spamming when a site remains down. Default is 14400 seconds (4 hours).        |
| `SiteChecks`         | A list of site check configurations. Each entry includes:                                                                                      |
|                      | - `url`: the website to monitor                                                                                                                |
|                      | - `interval`: how often to check (in seconds)                                                                                                  |
|                      | - `searchString`: text to look for in the HTTP response. Use wildcard `*` if any response content is acceptable.                               |

---

## Example Configuration

```json
{
  "RunOnce": false,
  "UseSlack": true,
  "SlackWebHookURL": "https://hooks.slack.com/services/...",
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

## Running Peek

Peek supports two modes of operation:

- **Continuous Mode**: Peek runs indefinitely and checks each configured site at the specified interval. This is the default.
- **Run Once Mode**: Peek performs a single round of checks and then exits. Ideal for solutions where scheduling is done via OS schedulers, like cron, Windows Task Scheduler, etc. Set `RunOnce` to `true` in the config file.

---

## Notes

- **Persistence**: Site status history is stored in a local NoSQL database (LiteDB).
- **Resilience**: Peek doesn’t restart itself; use `systemd`, `pm2`, or a scheduler to supervise it.