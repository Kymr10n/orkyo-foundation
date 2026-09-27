namespace Orkyo.Foundation.Tests;

/// <summary>
/// An <see cref="HttpMessageHandler"/> that answers every request with <paramref name="respond"/>
/// and records each request and its body. A responder that throws simulates a network failure.
/// </summary>
public sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    /// <summary>The body of each request in <see cref="Requests"/>, or empty when it had none.</summary>
    public List<string> Bodies { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
        Requests.Add(request);
        return respond(request);
    }
}
