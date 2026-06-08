namespace Peek.Tests;

public class StringExtensionsTests
{
    [Fact]
    public void Sanitize_null_returns_null()
    {
        string? input = null;
        Assert.Null(input.Sanitize());
    }
    [Theory]
    [InlineData("", "")]
    [InlineData("hello", "hello")]
    [InlineData(",hello", "hello")]
    [InlineData(",,hello", "hello")]
    [InlineData("  hello  ", "hello")]
    [InlineData(",  hello  ", "hello")]
    [InlineData("a,b,c", "a,b,c")]
    [InlineData(",", "")]
    [InlineData(",,,", "")]
    [InlineData(", ,", ",")]
    public void Sanitize_trims_leading_commas_and_whitespace(string input, string? expected)
    {
        Assert.Equal(expected, input.Sanitize());
    }
}
