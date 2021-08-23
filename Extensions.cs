using System;
using System.Net;

namespace Peek
{
    public static class StatusCodeExtensions
    {
        public static string ToStatusCodeText(this int statusCode)
        {
            string statusCodeText;
            // Negative status code means incorret content, but it's only relevant if the site is otherwise running
            if (statusCode < 0 && Math.Abs(statusCode) < 300)
            {
                var sc = (HttpStatusCode)Math.Abs(statusCode);
                statusCodeText = $"{sc} - Incorrect content";
            }
            else
            {
                var sc = (HttpStatusCode)Math.Abs(statusCode);
                statusCodeText = $"{sc}";
            }
            return statusCodeText;
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
            return s.TrimStart(new char[] { ',' }).TrimEnd(new char[] { '/' }).Trim();
        }
    }
}
