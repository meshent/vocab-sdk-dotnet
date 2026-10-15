using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Xunit;

namespace Vocab.Sdk.AspNetCore.Tests;

public class SignInSessionTests
{
    /// <summary>auth.vo.cab rotates refresh tokens: r1 is good once and refused for ever after.</summary>
    private static FakeAuthority RotatingAuthority(out Func<int> refreshCalls)
    {
        var calls = 0;
        var authority = new FakeAuthority()
            .On(HttpMethod.Post, "/auth/refresh", body =>
            {
                var presented = JsonDocument.Parse(body!).RootElement.GetProperty("refresh_token").GetString();
                var first = Interlocked.Increment(ref calls) == 1;
                return presented == "r1" && first
                    ? (HttpStatusCode.OK, """{"access_token":"a2","token_type":"Bearer","expires_in":3600,"refresh_token":"r2"}""")
                    : (HttpStatusCode.BadRequest, """{"error":"The refresh token has been used already; the session is revoked.","code":"invalid_grant"}""");
            })
            .On(HttpMethod.Post, "/oauth/revoke", _ => (HttpStatusCode.OK, "{}"));
        refreshCalls = () => calls;
        return authority;
    }

    [Fact]
    public async Task Signing_out_with_no_scheme_named_clears_the_session_and_revokes_the_refresh_token()
    {
        var authority = RotatingAuthority(out _);
        await using var app = await TestApp.StartAsync(authority, v => v.AddSignIn(), TestApp.MapSession);
        using var client = app.GetTestClient();
        var cookie = await TestApp.SignInAsync(client, expiresIn: 3600);

        using var request = TestApp.Request(HttpMethod.Post, "/signout", cookie);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(".Vocab.Session=", TestApp.SessionCookie(response));
        var revoke = Assert.Single(authority.Requests, r => r.Path == "/oauth/revoke");
        Assert.Equal("token=r1&client_secret=s3cret", revoke.Body);
        Assert.DoesNotContain(authority.Requests, r => r.Path.Contains("end_session", StringComparison.Ordinal) || r.Path.Contains("logout", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Concurrent_requests_near_expiry_share_one_refresh_and_a_late_one_reuses_it()
    {
        var authority = RotatingAuthority(out var refreshCalls);
        await using var app = await TestApp.StartAsync(authority, v => v.AddSignIn(), TestApp.MapSession);
        using var client = app.GetTestClient();
        var cookie = await TestApp.SignInAsync(client, expiresIn: 30);

        using var first = TestApp.Request(HttpMethod.Get, "/me", cookie);
        using var second = TestApp.Request(HttpMethod.Get, "/me", cookie);
        var responses = await Task.WhenAll(client.SendAsync(first), client.SendAsync(second));

        foreach (var response in responses)
        {
            Assert.Equal("authenticated:a2", await response.Content.ReadAsStringAsync());
            Assert.NotNull(TestApp.SessionCookie(response)); // renewed with the new tokens
            response.Dispose();
        }
        Assert.Equal(1, refreshCalls());

        // A request that still carries the pre-refresh cookie gets the same new tokens, not a second refresh.
        using var late = TestApp.Request(HttpMethod.Get, "/me", cookie);
        using var lateResponse = await client.SendAsync(late);
        Assert.Equal("authenticated:a2", await lateResponse.Content.ReadAsStringAsync());
        Assert.Equal(1, refreshCalls());
    }

    [Fact]
    public async Task A_refresh_token_the_authority_refuses_ends_the_session()
    {
        var authority = new FakeAuthority()
            .On(HttpMethod.Post, "/auth/refresh", _ => (HttpStatusCode.BadRequest, """{"error":"Signed out everywhere.","code":"invalid_grant"}"""));
        await using var app = await TestApp.StartAsync(authority, v => v.AddSignIn(), TestApp.MapSession);
        using var client = app.GetTestClient();
        var cookie = await TestApp.SignInAsync(client, expiresIn: 30);

        using var request = TestApp.Request(HttpMethod.Get, "/me", cookie);
        using var response = await client.SendAsync(request);

        Assert.Equal("anonymous", await response.Content.ReadAsStringAsync());
        Assert.Equal(".Vocab.Session=", TestApp.SessionCookie(response));
    }

    [Fact]
    public async Task Signing_out_of_a_session_whose_refresh_is_refused_ends_it_without_looping()
    {
        // Authenticate → refresh refused → signed out from inside validation → the endpoint signs out again. The
        // sign-out hook must not authenticate afresh here, or the two re-enter each other without end.
        var refreshCalls = 0;
        var authority = new FakeAuthority()
            .On(HttpMethod.Post, "/auth/refresh", _ =>
            {
                Interlocked.Increment(ref refreshCalls);
                return (HttpStatusCode.BadRequest, """{"error":"Signed out everywhere.","code":"invalid_grant"}""");
            })
            .On(HttpMethod.Post, "/oauth/revoke", _ => (HttpStatusCode.OK, "{}"));
        await using var app = await TestApp.StartAsync(authority, v => v.AddSignIn(), TestApp.MapSession);
        using var client = app.GetTestClient();
        var cookie = await TestApp.SignInAsync(client, expiresIn: 30);

        using var request = TestApp.Request(HttpMethod.Post, "/signout", cookie);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(".Vocab.Session=", TestApp.SessionCookie(response));
        Assert.DoesNotContain(authority.Requests, r => r.Path == "/oauth/revoke");
        Assert.Equal(1, refreshCalls);
    }

    [Fact]
    public async Task An_unreachable_authority_keeps_the_current_token_until_next_time()
    {
        var authority = new FakeAuthority()
            .On(HttpMethod.Post, "/auth/refresh", (_, _) => throw new HttpRequestException("connection refused"));
        await using var app = await TestApp.StartAsync(authority, v => v.AddSignIn(), TestApp.MapSession);
        using var client = app.GetTestClient();
        var cookie = await TestApp.SignInAsync(client, expiresIn: 30);

        using var request = TestApp.Request(HttpMethod.Get, "/me", cookie);
        using var response = await client.SendAsync(request);

        Assert.Equal("authenticated:a1", await response.Content.ReadAsStringAsync());
        Assert.Null(TestApp.SessionCookie(response));
    }
}
