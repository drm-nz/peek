namespace Peek.Tests;

public class CheckResultTests
{
    [Theory]
    [InlineData(200, "*", "any content")]
    [InlineData(200, "*", null)]
    [InlineData(200, null, "any content")]
    [InlineData(200, null, null)]
    [InlineData(500, "*", "error page")]
    [InlineData(500, null, null)]
    public void wildcard_or_null_search_returns_raw_status(
        int httpStatus, string? searchString, string? responseContent)
    {
        var result = Program.DetermineStatusWithContentCheck(httpStatus, responseContent, searchString);
        Assert.Equal(httpStatus, result);
    }

    [Theory]
    [InlineData(200, "Welcome", "Welcome to the site!", 200)]
    [InlineData(200, "Welcome", "welcome to the site!", -200)]
    [InlineData(200, "foo", "foobar", 200)]
    [InlineData(200, "foobar", "foo", -200)]
    [InlineData(404, "Welcome", "Welcome", 404)]
    [InlineData(500, "Hello", "Hello World", 500)]
    [InlineData(500, "missing", "Hello World", -500)]
    public void content_match_determines_negative_status(
        int httpStatus, string? searchString, string? responseContent, int expected)
    {
        var result = Program.DetermineStatusWithContentCheck(httpStatus, responseContent, searchString);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void null_responseContent_with_search_returns_raw_status()
    {
        var result = Program.DetermineStatusWithContentCheck(200, null, "foo");
        Assert.Equal(200, result);
    }

    [Fact]
    public void empty_responseContent_with_search_returns_negative()
    {
        var result = Program.DetermineStatusWithContentCheck(200, "", "foo");
        Assert.Equal(-200, result);
    }
}
