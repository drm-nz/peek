# Peek

A lightweight, self-hosted website availability monitor with optional Slack integration — a simple alternative to updown.io. Built with .NET 10 and LiteDB.

Peek performs one round of health checks against the configured sites and exits, making it ideal for use with `cron`, `systemd` timers, or any scheduler.

---

## Configuration

All configuration lives in `appsettings.json`.

| Setting                | Default              | Description                                                                                                                 |
|------------------------|----------------------|-----------------------------------------------------------------------------------------------------------------------------|
| `RunOnce`              | `true`               | **Currently unused** — Peek always runs once and exits. Reserved for a future continuous mode.                              |
| `UseSlack`             | `false`              | Enable or disable Slack notifications.                                                                                      |
| `SlackWebHookURL`      | `""`                 | Slack Incoming Webhook URL. Can be overridden via the `PEEK_SLACK_WEBHOOK_URL` environment variable.                        |
| `SlackChannel`         | `"#notifications"`   | The Slack channel to post notifications to.                                                                                 |
| `SlackReportInterval`  | `14400` (4 hours)    | Minimum seconds between Slack notifications for the same site to prevent spam while it stays down.                          |
| `HttpTimeoutSeconds`   | `15`                 | HTTP request timeout in seconds (clamped to 1–120).                                                                         |
| `MaxConcurrency`       | `1`                  | Maximum number of site checks to run in parallel. Set > 1 for faster checks against many sites.                             |
| `DbPath`               | `"Peek.db"`          | Path to the LiteDB database file that stores site check state (history, last status, next check time).                      |
| `SiteChecks`           | —                    | An array of site check definitions (see below).                                                                              |

### SiteCheck entries

Each entry in `SiteChecks` defines a single monitored endpoint:

| Field          | Description                                                                                  |
|----------------|----------------------------------------------------------------------------------------------|
| `url`          | The URL to check (must be a valid `http://` or `https://` URL).                              |
| `interval`     | Minimum seconds between checks. Must be ≥ 5.                                                 |
| `searchString` | A substring to search for in the HTTP response body. Use `"*"` to skip content validation.   |

---

## Example Configuration

```json
{
  "UseSlack": true,
  "SlackWebHookURL": "https://hooks.slack.com/services/...",
  "SlackChannel": "#notifications",
  "SlackReportInterval": 14400,
  "RunOnce": true,
  "HttpTimeoutSeconds": 15,
  "MaxConcurrency": 1,
  "DbPath": "Peek.db",
  "SiteChecks": [
    {
      "url": "https://example.com",
      "interval": 60,
      "searchString": "Example Domain"
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

Peek always runs once, checks all due sites, and exits.

```bash
dotnet run --project Peek.csproj
```

**Exit codes:**

- `0` — All checks passed (or no sites were due).
- `1` — One or more sites failed validation or returned an error status (≥ 400).

Exit codes make it easy to use Peek with cron, systemd timers, or CI pipelines.

---

## Logging

Peek logs to both the console and daily rolling files:

- **Console:** Standard output with `[INFO]`, `[WARN]`, `[ERROR]` levels.
- **File:** `logs/peek-YYYY-MM-DD.log` (created automatically in the working directory).

Each log line includes the check result, response time in milliseconds, and any error messages.

---

## Slack Notifications

When `UseSlack` is `true`, Peek sends notifications to the configured Slack channel for:

- **Site down** — status transitions from healthy (≤ 399) to error (≥ 400).
- **Recovery** — status transitions back to healthy.
- **Information** — persistent errors with additional messages (rate-limited by `SlackReportInterval`).
- **Status changes** — any other change in status code.

The webhook URL can be set either in `appsettings.json` or via the `PEEK_SLACK_WEBHOOK_URL` environment variable (the environment variable takes precedence).

---

## How It Works

1. **Config validation** — `appsettings.json` is checked for missing URLs, invalid intervals, and missing webhook when Slack is enabled.
2. **Database sync** — Site check definitions are synced into LiteDB. New sites are inserted; removed sites are cleaned up.
3. **Site checking** — Each due site is fetched via HTTP. Checks include:
   - Response status code validation.
   - Content matching against `searchString`.
   - **Automatic retry** — failed requests are retried once after 5 seconds.
   - **Certificate expiry warnings** — logged if a certificate expires within 30 days.
   - **Response time** — measured and logged in milliseconds.
4. **State persistence** — Results are written back to LiteDB, including the last status, messages, and next check schedule.
5. **Slack notifications** — Sent for state transitions when enabled (rate-limited per site).
6. **Exit** — Peek exits with code `0` (all OK) or `1` (failures detected).

---

## Running on a Schedule

Because Peek runs once and exits by design, use an external scheduler to run it periodically:

### cron (Linux/macOS)

```cron
*/5 * * * * cd /path/to/peek && dotnet run --project Peek.csproj
```

### systemd timer

Create a service and timer unit to run Peek every 5 minutes.

---

## Notes

- **Persistence**: Site status history is stored in a local LiteDB database (`Peek.db` by default).
- **Resilience**: Peek doesn't restart itself; use `cron`, `systemd`, or a scheduler to supervise it.
- **Concurrency**: Set `MaxConcurrency` to check multiple sites in parallel. Use with caution to avoid rate-limiting or overloading targets.
- **Environment variable**: `PEEK_SLACK_WEBHOOK_URL` overrides the `SlackWebHookURL` config setting.
- **Retry behaviour**: Failed HTTP requests are automatically retried once after 5 seconds.
- **Content verification**: Set `searchString` to verify response body content. Use `"*"` to skip content checks. Mismatches are reported as negative status codes.

---

## Development

### Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

### Build

```bash
dotnet build
```

### Test

```bash
dotnet test
```

The test suite includes 139 unit tests covering configuration validation, site checking, retry logic, content matching, Slack notifications, certificate validation, logging, and database synchronisation. Code coverage exceeds 90%.

### Publish

```bash
dotnet publish -c Release -o ./publish
```
