using System.Net;

namespace Peek.Tests;

public class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpResponseMessage>> _responses = new();
    private Func<HttpRequestMessage, HttpResponseMessage>? _handler;

    public MockHttpMessageHandler(HttpStatusCode statusCode, string content = "")
    {
        _responses.Enqueue(() => new HttpResponseMessage(statusCode) { Content = new StringContent(content) });
    }

    public MockHttpMessageHandler(params HttpStatusCode[] statusCodes)
    {
        foreach (var sc in statusCodes)
            _responses.Enqueue(() => new HttpResponseMessage(sc));
    }

    public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        _handler = handler;
    }

    public List<HttpRequestMessage> ReceivedRequests { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken _)
    {
        ReceivedRequests.Add(request);

        if (_handler != null)
            return Task.FromResult(_handler(request));

        var response = _responses.Count > 0
            ? _responses.Dequeue()()
            : new HttpResponseMessage(HttpStatusCode.NotFound);

        return Task.FromResult(response);
    }

    public static IDisposable ReplaceClientWith(MockHttpMessageHandler handler)
    {
        var original = Program.Client;
        Program.Client = new HttpClient(handler);
        return new Restorer(() => Program.Client = original);
    }

    private class Restorer : IDisposable
    {
        private readonly Action _restore;
        public Restorer(Action restore) => _restore = restore;
        public void Dispose() => _restore();
    }
}
