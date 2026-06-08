namespace Peek.Tests;

public class DateTimeExtensionsTests
{
    [Fact]
    public void FormattedDate_returns_expected_format()
    {
        var date = new DateTime(2026, 6, 8, 9, 30, 15);
        Assert.Equal("2026-06-08 09:30:15", date.FormattedDate());
    }

    [Fact]
    public void FormattedDate_handles_single_digit_month_and_day()
    {
        var date = new DateTime(2026, 1, 5, 3, 5, 7);
        Assert.Equal("2026-01-05 03:05:07", date.FormattedDate());
    }

    [Fact]
    public void FormattedDate_handles_midnight()
    {
        var date = new DateTime(2026, 12, 25, 0, 0, 0);
        Assert.Equal("2026-12-25 00:00:00", date.FormattedDate());
    }
}
