namespace Peek.Tests;

public class StatusCodeExtensionsTests
{
    [Theory]
    [InlineData(200, "OK")]
    [InlineData(201, "Created")]
    [InlineData(301, "MovedPermanently")]
    [InlineData(404, "NotFound")]
    [InlineData(500, "InternalServerError")]
    [InlineData(503, "ServiceUnavailable")]
    [InlineData(418, "418")]
    public void ToStatusCodeText_positive_returns_enum_name(int statusCode, string expected)
    {
        Assert.Equal(expected, statusCode.ToStatusCodeText());
    }

    [Theory]
    [InlineData(-200, "OK - Incorrect content")]
    [InlineData(-201, "Created - Incorrect content")]
    [InlineData(-299, "299 - Incorrect content")]
    public void ToStatusCodeText_negative_under_300_appends_incorrect_content(int statusCode, string expected)
    {
        Assert.Equal(expected, statusCode.ToStatusCodeText());
    }

    [Theory]
    [InlineData(-404, "NotFound")]
    [InlineData(-500, "InternalServerError")]
    [InlineData(-301, "MovedPermanently")]
    public void ToStatusCodeText_negative_over_300_returns_enum_name_only(int statusCode, string expected)
    {
        Assert.Equal(expected, statusCode.ToStatusCodeText());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(999)]
    public void ToStatusCodeText_unknown_code_does_not_throw(int statusCode)
    {
        var result = statusCode.ToStatusCodeText();
        Assert.NotNull(result);
    }
}
