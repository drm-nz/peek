using System.Net;

namespace Peek.Tests;

public class CheckSiteTests : IDisposable
{
    private readonly SiteCheck _site;
    private readonly IDisposable _clientRestore;

    public CheckSiteTests()
    {
        Log.Reset();
        _site = new SiteCheck
        {
            URL = "https://example.com",
            Interval = 60,
            SearchString = "*"
        };
        _clientRestore = TestState.CaptureClient();
    }

    public void Dispose()
    {
        _clientRestore.Dispose();
    }

    [Fact]
    public async Task successful_check_returns_200()
    {
        Program.Client = new HttpClient(new MockHttpMessageHandler(HttpStatusCode.OK, "Hello World"));
        await Program.CheckSiteAsync(_site);
        Assert.Equal(200, _site.LastState);
    }

    [Fact]
    public async Task successful_check_sets_next_check()
    {
        Program.Client = new HttpClient(new MockHttpMessageHandler(HttpStatusCode.OK, "Hello World"));
        var before = DateTime.Now;
        await Program.CheckSiteAsync(_site);
        var after = DateTime.Now;
        Assert.True(_site.NextCheck >= before.AddSeconds(60));
        Assert.True(_site.NextCheck <= after.AddSeconds(60));
    }

    [Fact]
    public async Task search_string_match_keeps_positive_status()
    {
        Program.Client = new HttpClient(new MockHttpMessageHandler(HttpStatusCode.OK, "Hello World"));
        _site.SearchString = "Hello";
        await Program.CheckSiteAsync(_site);
        Assert.Equal(200, _site.LastState);
    }

    [Fact]
    public async Task search_string_mismatch_returns_negative_status()
    {
        Program.Client = new HttpClient(new MockHttpMessageHandler(HttpStatusCode.OK, "Hello World"));
        _site.SearchString = "NonExistent";
        await Program.CheckSiteAsync(_site);
        Assert.Equal(-200, _site.LastState);
    }

    [Fact]
    public async Task http_error_returns_error_status()
    {
        Program.Client = new HttpClient(new MockHttpMessageHandler(HttpStatusCode.InternalServerError));
        await Program.CheckSiteAsync(_site);
        Assert.Equal(500, _site.LastState);
    }

    [Fact]
    public async Task not_found_returns_404()
    {
        Program.Client = new HttpClient(new MockHttpMessageHandler(HttpStatusCode.NotFound));
        await Program.CheckSiteAsync(_site);
        Assert.Equal(404, _site.LastState);
    }

    [Fact]
    public async Task search_string_mismatch_on_error_negates_status()
    {
        Program.Client = new HttpClient(new MockHttpMessageHandler(HttpStatusCode.NotFound, "Oops"));
        _site.SearchString = "Welcome";
        await Program.CheckSiteAsync(_site);
        Assert.Equal(-404, _site.LastState);
    }

    [Fact]
    public async Task retry_succeeds_on_second_attempt()
    {
        Program.RetryDelayMs = 1;
        var attemptCount = 0;
        Program.Client = new HttpClient(new MockHttpMessageHandler(_ =>
        {
            attemptCount++;
            return attemptCount == 1
                ? throw new HttpRequestException("First failed")
                : new HttpResponseMessage(HttpStatusCode.OK);
        }));
        await Program.CheckSiteAsync(_site);
        Assert.Equal(2, attemptCount);
        Assert.Equal(200, _site.LastState);
    }

    [Fact]
    public async Task retry_fails_on_both_attempts()
    {
        Program.RetryDelayMs = 1;
        Program.Client = new HttpClient(new MockHttpMessageHandler(_ =>
            throw new HttpRequestException("Always fails")));
        await Program.CheckSiteAsync(_site);
        Assert.Equal(410, _site.LastState);
        Assert.Contains("Always fails", _site.Message);
    }

    [Fact]
    public async Task exception_during_content_reading_captures_message()
    {
        Program.Client = new HttpClient(new MockHttpMessageHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ThrowingContent() };
            return response;
        }));
        await Program.CheckSiteAsync(_site);
        Assert.Contains("Read failed", _site.Message);
    }

    private class ThrowingContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => throw new InvalidOperationException("Read failed");
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
    }
}
