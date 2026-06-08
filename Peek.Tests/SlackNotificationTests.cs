namespace Peek.Tests;

public class SlackNotificationTests
{
    private static readonly DateTime Future = DateTime.UtcNow.AddDays(1);
    private static readonly DateTime Past = DateTime.UtcNow.AddDays(-1);

    [Theory]
    [InlineData(200, 500, "down", "danger")]
    [InlineData(200, 503, "down", "danger")]
    [InlineData(200, 502, "down", "danger")]
    [InlineData(0, 500, "down", "danger")]
    public void status_goes_down_when_moving_from_ok_to_error(
        int prev, int curr, string expectedStatus, string expectedColour)
    {
        var (status, colour) = Program.DetermineSlackStatus(prev, curr, "", Future, DateTime.UtcNow);
        Assert.Equal(expectedStatus, status);
        Assert.Equal(expectedColour, colour);
    }

    [Theory]
    [InlineData(500, 200, "recovered", "good")]
    [InlineData(503, 200, "recovered", "good")]
    [InlineData(502, 201, "recovered", "good")]
    [InlineData(500, 0, "recovered", "good")]
    public void status_recovered_when_moving_from_error_to_ok(
        int prev, int curr, string expectedStatus, string expectedColour)
    {
        var (status, colour) = Program.DetermineSlackStatus(prev, curr, "", Future, DateTime.UtcNow);
        Assert.Equal(expectedStatus, status);
        Assert.Equal(expectedColour, colour);
    }

    [Theory]
    [InlineData(200, -200, "down", "danger")]
    [InlineData(201, -201, "down", "danger")]
    [InlineData(0, -200, "status change", "warning")]
    public void status_down_when_content_mismatch_appears(
        int prev, int curr, string expectedStatus, string expectedColour)
    {
        var (status, colour) = Program.DetermineSlackStatus(prev, curr, "", Future, DateTime.UtcNow);
        Assert.Equal(expectedStatus, status);
        Assert.Equal(expectedColour, colour);
    }

    [Theory]
    [InlineData(-200, 200, "recovered", "good")]
    [InlineData(-201, 201, "recovered", "good")]
    [InlineData(-200, 0, "status change", "warning")]
    public void status_recovered_when_content_match_returns(
        int prev, int curr, string expectedStatus, string expectedColour)
    {
        var (status, colour) = Program.DetermineSlackStatus(prev, curr, "", Future, DateTime.UtcNow);
        Assert.Equal(expectedStatus, status);
        Assert.Equal(expectedColour, colour);
    }

    [Fact]
    public void information_when_both_ok_with_message_and_cooldown_expired()
    {
        var (status, colour) = Program.DetermineSlackStatus(200, 200, "certificate expiring", Past, DateTime.UtcNow);
        Assert.Equal("information", status);
        Assert.Equal("warning", colour);
    }

    [Fact]
    public void no_notification_when_both_ok_with_message_but_cooldown_active()
    {
        var (status, colour) = Program.DetermineSlackStatus(200, 200, "certificate expiring", Future, DateTime.UtcNow);
        Assert.Null(status);
        Assert.Null(colour);
    }

    [Fact]
    public void no_notification_when_both_ok_without_message()
    {
        var (status, colour) = Program.DetermineSlackStatus(200, 200, "", Future, DateTime.UtcNow);
        Assert.Null(status);
        Assert.Null(colour);
    }

    [Theory]
    [InlineData(200, 201, "status change", "warning")]
    [InlineData(200, 302, "status change", "warning")]
    [InlineData(500, 502, "status change", "warning")]
    [InlineData(404, 500, "status change", "warning")]
    [InlineData(200, 0, "status change", "warning")]
    public void status_change_when_both_in_same_band_but_different(
        int prev, int curr, string expectedStatus, string expectedColour)
    {
        var (status, colour) = Program.DetermineSlackStatus(prev, curr, "", Future, DateTime.UtcNow);
        Assert.Equal(expectedStatus, status);
        Assert.Equal(expectedColour, colour);
    }

    [Fact]
    public void no_notification_when_identical_state_and_no_message()
    {
        var (status, colour) = Program.DetermineSlackStatus(200, 200, "", Future, DateTime.UtcNow);
        Assert.Null(status);
        Assert.Null(colour);
    }

    [Fact]
    public void information_takes_priority_over_status_change()
    {
        var (status, colour) = Program.DetermineSlackStatus(200, 201, "certificate expiring", Past, DateTime.UtcNow);
        Assert.Equal("information", status);
    }

    [Fact]
    public void down_takes_priority_over_information()
    {
        var (status, colour) = Program.DetermineSlackStatus(200, 500, "certificate expiring", Past, DateTime.UtcNow);
        Assert.Equal("down", status);
    }

    [Fact]
    public void recovered_takes_priority_over_information()
    {
        var (status, colour) = Program.DetermineSlackStatus(500, 200, "some message", Past, DateTime.UtcNow);
        Assert.Equal("recovered", status);
    }
}
