using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace Vocab.Sdk.Tests;

/// <summary>A scripted HTTP handler: records every request and answers with what the test queued.</summary>
internal sealed class FakeApi : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _answers = new();

    public List<(HttpRequestMessage Request, string? Body)> Requests { get; } = [];

    public FakeApi Answer(HttpStatusCode status, string? json = null, Action<HttpResponseMessage>? decorate = null)
    {
        _answers.Enqueue(_ =>
        {
            var response = new HttpResponseMessage(status);
            if (json is not null)
                response.Content = new StringContent(json, Encoding.UTF8, "application/json");
            decorate?.Invoke(response);
            return response;
        });
        return this;
    }

    public HttpClient Client(string baseAddress = "https://example.api.vo.cab/") =>
        new(this) { BaseAddress = new Uri(baseAddress) };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request, body));
        if (_answers.Count == 0)
            throw new InvalidOperationException($"No answer queued for {request.Method} {request.RequestUri}.");
        return _answers.Dequeue()(request);
    }

    public static string? BearerOf(HttpRequestMessage request) =>
        request.Headers.Authorization is { Scheme: "Bearer" } auth ? auth.Parameter : null;

    public static MediaTypeHeaderValue? ContentTypeOf(HttpRequestMessage request) => request.Content?.Headers.ContentType;
}
