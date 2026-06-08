# Peek

A lightweight, self-hosted website uptime monitoring tool — a minimal alternative to services like UpDown.io. Peek periodically checks a list of configured URLs, validates responses, persists state to a local LiteDB database, and optionally sends Slack notifications when a site goes down or recovers.

---

## Configuration

| Setting | Description |
|---|---|
| `RunOnce` | If `true`, Peek runs one batch of checks and exits (ideal for cron). If `false`, runs continuously in a loop. |
| `UseSlack` | Enable Slack notifications. |
| `SlackWebHookURL` | Slack Incoming Webhook URL. Required if `UseSlack` is `true`. Can be overridden by the `PEEK_SLACK_WEBHOOK_URL` environment variable. |
| `SlackChannel` | Slack channel to post notifications to (default `#notifications`). |
| `SlackReportInterval` | Minimum seconds between Slack notifications for non-critical messages (default `14400` = 4 hours). |
| `HttpTimeoutSeconds` | HTTP request timeout in seconds (default `15`, max `120`). |
| `MaxConcurrency` | Number of sites to check in parallel (default `1` = sequential). Increase for 50+ sites. |
| `DbPath` | Path to the LiteDB database file (default `Peek.db`). |
| `SiteChecks` | Array of sites to monitor. Each entry: |
| | - `url` — the website to check |
| | - `interval` — how often to check (seconds, minimum 5) |
| | - `searchString` — text required in the response body. Use `"*"` to accept any content. |

### Example

```json
{
  "RunOnce": true,
  "UseSlack": true,
  "SlackWebHookURL": "https://hooks.slack.com/services/...",
  "SlackChannel": "#alerts",
  "SlackReportInterval": 14400,
  "HttpTimeoutSeconds": 15,
  "MaxConcurrency": 5,
  "DbPath": "/data/peek/Peek.db",
  "SiteChecks": [
    { "url": "https://example.com", "interval": 60, "searchString": "Welcome" },
    { "url": "https://api.example.com", "interval": 30, "searchString": "*" }
  ]
}
```

### Environment Variables

| Variable | Overrides |
|---|---|
| `PEEK_SLACK_WEBHOOK_URL` | `SlackWebHookURL` in config (use this instead of storing secrets in config files) |

---

## Running

```bash
# Run once (cron-friendly)
dotnet run --project Peek.csproj

# Publish a self-contained binary
dotnet publish -c Release -r linux-x64 --self-contained true
./bin/Release/net9.0/linux-x64/publish/peek
```

### Modes

- **Run-Once** (`RunOnce: true`): Performs a single round of due checks, logs results, sends notifications, and exits. Designed for cron, systemd timers, or similar schedulers.
- **Continuous** (`RunOnce: false`): Runs indefinitely in a loop, checking each site at its configured interval.

### Logging

Peek writes structured logs to both:
- **stdout** — captured by cron / systemd
- **`logs/peek-YYYY-MM-DD.log`** — daily rolling files, 3 log levels (INFO, WARN, ERROR)

---

## Behaviour

- **Retry**: If a request fails due to a network error, Peek retries once after 5 seconds before marking the site as down.
- **Certificate expiry**: If an SSL certificate expires within 30 days, a warning is logged (no alert by default).
- **Content validation**: If `searchString` is set (not `"*"`), the response body must contain that string. A mismatch is reported with a negated status code (e.g., `-200`).
- **State persistence**: Site status and check schedules are persisted in LiteDB, so restarts don't cause duplicate alerts.

---

## Exit Codes

| Code | Meaning |
|---|---|
| `0` | All checks passed |
| `1` | One or more sites failed |

Cron, systemd, and CI systems use this to trigger alerts.

---

## Build Requirements

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)

```bash
git clone <repo>
cd peek
dotnet build
dotnet run --project Peek.csproj
```
