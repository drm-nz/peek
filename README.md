# Peek
A simple self-hosted updown.io alternative for monitoring website availability with optional Slack integration.

# Configuration options
* DbConnectionString - Connection string for LiteDB. Essentially this is the name of the NoSQL database file where we store site states. Default: Peak.db
* UseSlack - Enable or disable Slack notifications. Default: false
* SlackReportInterval - Number of seconds between Slack notifications on continuous errors, for example, if a site is down. Default: 14400 (4h)
* SlackWebHookURL - Your own Slack webhook URL where Peek will send its notifications to, if enabled.
* RunOnce - Enable or disable continuous monitoring. When true, Peek only goes through the list of sites once, then exits. This may be useful if you want to schedule monitoring differently, ie. from scheduled tasks or cron. Default: false
* SiteChecks - The list of sites to monitor.
  * url - The URL of the website you want to monitor.
  * interval - Number of seconds between checks.
  * searchString - Search string to look for in the returned HTML source. This is to check the the site isn't just loading, but it is actually loading the expected content. Asterisk means that any content is acceptable, ie. we don't care.
