using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace Vocab.Sdk.AspNetCore.Tests;

/// <summary>A thread-safe stand-in for auth.vo.cab: routes by method and path, records every request.</summary>
internal sealed class FakeAuthority : HttpMessageHandler
{
    private readonly ConcurrentDictionary<string, Func<HttpRequestMessage, string?, Task<HttpResponseMessage>>> _routes = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<(string Method, string Path, string? Body)> _requests = new();

    public IReadOnlyList<(string Method, string Path, string? Body)> Requests => [.. _requests];

    public FakeAuthority On(HttpMethod method, string path, Func<HttpRequestMessage, string?, Task<HttpResponseMessage>> answer)
    {
        _routes[$"{method.Method} {path}"] = answer;
        return this;
    }

    public FakeAuthority On(HttpMethod method, string path, Func<string?, (HttpStatusCode Status, string Json)> answer) =>
        On(method, path, (_, body) =>
        {
            var (status, json) = answer(body);
            return Task.FromResult(Json(status, json));
        });

    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var path = request.RequestUri!.AbsolutePath;
        _requests.Enqueue((request.Method.Method, path, body));
        if (!_routes.TryGetValue($"{request.Method.Method} {path}", out var answer))
            return Json(HttpStatusCode.NotFound, """{"error":"no such route","code":"not_found"}""");
        return await answer(request, body);
    }
}
