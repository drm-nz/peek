using System;
using System.Net;

namespace Peek;

public static class StatusCodeExtensions
{
    public static string ToStatusCodeText(this int statusCode)
    {
        var sc = (HttpStatusCode)Math.Abs(statusCode);
        if (statusCode < 0 && Math.Abs(statusCode) < 300)
            return $"{sc} - Incorrect content";
        return $"{sc}";
    }
}

public static class DateTimeExtensions
{
    public static string FormattedDate(this DateTime date)
    {
        return date.ToString("yyyy-MM-dd HH:mm:ss");
    }
}

public static class StringExtensions
{
    public static string Sanitize(this string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return s.TrimStart(',').Trim();
    }
}
