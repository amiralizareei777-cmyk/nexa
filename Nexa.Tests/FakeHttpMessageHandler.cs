using System.Net;
using System.Text;

namespace Nexa.Tests;

/// <summary>
/// Injectable HttpClient handler that returns canned responses for provider tests.
/// </summary>
public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;

    public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        _responder = (request, _) => Task.FromResult(responder(request));
    }

    public FakeHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
    {
        _responder = responder;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return _responder(request, cancellationToken);
    }

    public static HttpClient Client(string jsonBody, HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
        });
        return new HttpClient(handler);
    }

    public static HttpClient ThatThrows(Exception exception)
    {
        var handler = new FakeHttpMessageHandler(_ => throw exception);
        return new HttpClient(handler);
    }
}
