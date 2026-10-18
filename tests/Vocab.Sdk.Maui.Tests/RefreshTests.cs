using System.Net;
using System.Text.Json;
using Xunit;

namespace Vocab.Sdk.Maui.Tests;

public class RefreshTests
{
    [Fact]
    public async Task A_token_well_before_expiry_is_returned_as_is()
    {
        var h = new Harness();
        await h.StoreSessionAsync(TimeSpan.FromMinutes(30));
        using var signIn = h.Build();

        Assert.Equal("a1", await signIn.GetAccessTokenAsync());
        Assert.Empty(h.Server.Requests);
    }

    [Fact]
    public async Task A_token_near_expiry_is_refreshed_once_however_many_callers_race_and_the_rotated_tokens_are_stored()
    {
        var h = new Harness();
        await h.StoreSessionAsync(TimeSpan.FromSeconds(30));
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var bodies = new List<string?>();
        h.Server.On(HttpMethod.Post, "/auth/refresh", async (_, body) =>
        {
            Interlocked.Increment(ref calls);
            lock (bodies)
                bodies.Add(body);
            await gate.Task;
            return FakeAuthority.Json(HttpStatusCode.OK, Harness.Tokens("a2", "r2"));
        });
        using var signIn = h.Build();

        var callers = Enumerable.Range(0, 8).Select(_ => signIn.GetAccessTokenAsync().AsTask()).ToArray();
        await Task.Delay(50);
        Assert.Equal(1, calls);
        Assert.All(callers, t => Assert.False(t.IsCompleted));

        gate.SetResult();
        var tokens = await Task.WhenAll(callers);

        Assert.All(tokens, t => Assert.Equal("a2", t));
        Assert.Equal(1, calls);
        var sent = JsonSerializer.Deserialize<Dictionary<string, string>>(bodies.Single()!)!;
        Assert.Equal("r1", sent["refresh_token"]);
        Assert.False(sent.ContainsKey("client_secret"));

        var stored = await h.Store.LoadAsync();
        Assert.Equal("a2", stored!.AccessToken);
        Assert.Equal("r2", stored.RefreshToken);
        Assert.Equal(h.Clock.Now.AddHours(1), stored.ExpiresAt);
        Assert.Single(h.Changes);

        // Later callers, in the same expiry window, find the fresh session and spend nothing.
        Assert.Equal("a2", await signIn.GetAccessTokenAsync());
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task A_caller_that_loaded_the_old_session_before_a_refresh_completes_does_not_replay_the_old_refresh_token()
    {
        // Two callers, both past the fast path with the same stale session. The second must not refresh with r1
        // after the first already rotated it away; auth.vo.cab would treat that as theft and revoke the session.
        var h = new Harness();
        await h.StoreSessionAsync(TimeSpan.FromSeconds(30));
        var refreshed = new List<string>();
        h.Server.On(HttpMethod.Post, "/auth/refresh", body =>
        {
            var token = JsonSerializer.Deserialize<Dictionary<string, string>>(body!)!["refresh_token"];
            lock (refreshed)
                refreshed.Add(token);
            return (HttpStatusCode.OK, Harness.Tokens("a2", "r2"));
        });
        using var signIn = h.Build();

        var first = await signIn.GetAccessTokenAsync();
        var second = await signIn.GetAccessTokenAsync();

        Assert.Equal("a2", first);
        Assert.Equal("a2", second);
        Assert.Equal(["r1"], refreshed);
    }

    [Fact]
    public async Task An_already_expired_token_is_refreshed_too()
    {
        var h = new Harness();
        await h.StoreSessionAsync(TimeSpan.FromMinutes(-10));
        h.Server.On(HttpMethod.Post, "/auth/refresh", _ => (HttpStatusCode.OK, Harness.Tokens("a2", "r2")));
        using var signIn = h.Build();

        Assert.Equal("a2", await signIn.GetAccessTokenAsync());
    }

    [Fact]
    public async Task A_refresh_token_auth_no_longer_accepts_ends_the_session_locally()
    {
        var h = new Harness();
        await h.StoreSessionAsync(TimeSpan.FromSeconds(30));
        h.Server.On(HttpMethod.Post, "/auth/refresh", _ => (HttpStatusCode.BadRequest, """{"error":"The session was revoked.","code":"invalid_grant"}"""));
        using var signIn = h.Build();

        Assert.Null(await signIn.GetAccessTokenAsync());

        Assert.Null(await signIn.GetSessionAsync());
        Assert.False(await signIn.IsSignedInAsync());
        Assert.Empty(h.Storage.Entries);
        Assert.Equal([null], h.Changes);
    }

    [Fact]
    public async Task When_auth_is_unreachable_a_still_valid_token_is_returned_and_the_refresh_retried_next_time()
    {
        var h = new Harness();
        await h.StoreSessionAsync(TimeSpan.FromSeconds(30));
        var calls = 0;
        h.Server.On(HttpMethod.Post, "/auth/refresh", (_, _) =>
        {
            calls++;
            throw new HttpRequestException("offline");
        });
        using var signIn = h.Build();

        Assert.Equal("a1", await signIn.GetAccessTokenAsync());
        Assert.Equal("a1", await signIn.GetAccessTokenAsync());

        Assert.Equal(2, calls);
        Assert.Equal("r1", (await h.Store.LoadAsync())!.RefreshToken);
        Assert.Empty(h.Changes);
    }

    [Fact]
    public async Task When_auth_is_unreachable_and_the_token_has_expired_the_failure_is_thrown_and_the_session_kept()
    {
        var h = new Harness();
        await h.StoreSessionAsync(TimeSpan.FromMinutes(-1));
        h.Server.On(HttpMethod.Post, "/auth/refresh", (_, _) => throw new HttpRequestException("offline"));
        using var signIn = h.Build();

        await Assert.ThrowsAsync<HttpRequestException>(async () => await signIn.GetAccessTokenAsync());

        Assert.NotNull(await h.Store.LoadAsync());
    }

    [Fact]
    public async Task A_server_error_on_refresh_is_not_a_sign_out()
    {
        var h = new Harness();
        await h.StoreSessionAsync(TimeSpan.FromSeconds(30));
        h.Server.On(HttpMethod.Post, "/auth/refresh", _ => (HttpStatusCode.ServiceUnavailable, """{"error":"busy","code":"unknown"}"""));
        using var signIn = h.Build();

        Assert.Equal("a1", await signIn.GetAccessTokenAsync());
        Assert.NotNull(await h.Store.LoadAsync());
    }

    [Fact]
    public async Task Without_a_refresh_token_the_session_lasts_exactly_until_the_access_token_expires()
    {
        var h = new Harness();
        await h.StoreSessionAsync(TimeSpan.FromSeconds(30), refreshToken: null);
        using var signIn = h.Build();

        Assert.Equal("a1", await signIn.GetAccessTokenAsync());

        h.Clock.Now += TimeSpan.FromSeconds(31);
        Assert.Null(await signIn.GetAccessTokenAsync());
        Assert.Empty(h.Server.Requests);
        Assert.Empty(h.Storage.Entries);
    }

    [Fact]
    public async Task A_refresh_answer_without_a_rotated_refresh_token_keeps_the_current_one()
    {
        var h = new Harness();
        await h.StoreSessionAsync(TimeSpan.FromSeconds(30));
        h.Server.On(HttpMethod.Post, "/auth/refresh", _ => (HttpStatusCode.OK, Harness.Tokens("a2", refresh: null)));
        using var signIn = h.Build();

        Assert.Equal("a2", await signIn.GetAccessTokenAsync());
        Assert.Equal("r1", (await h.Store.LoadAsync())!.RefreshToken);
    }

    [Fact]
    public async Task Refresh_before_is_honoured()
    {
        var h = new Harness();
        h.SignInOptions.RefreshBefore = TimeSpan.FromMinutes(10);
        await h.StoreSessionAsync(TimeSpan.FromMinutes(9));
        h.Server.On(HttpMethod.Post, "/auth/refresh", _ => (HttpStatusCode.OK, Harness.Tokens("a2")));
        using var signIn = h.Build();

        Assert.Equal("a2", await signIn.GetAccessTokenAsync());
    }

    [Fact]
    public async Task Signed_out_means_no_token_and_no_request()
    {
        var h = new Harness();
        using var signIn = h.Build();

        Assert.Null(await signIn.GetAccessTokenAsync());
        Assert.Empty(h.Server.Requests);
    }

    [Fact]
    public async Task New_tokens_the_store_cannot_persist_take_effect_in_memory_so_the_old_refresh_token_is_never_replayed()
    {
        var h = new Harness();
        await h.StoreSessionAsync(TimeSpan.FromSeconds(30));
        var refreshed = new List<string>();
        h.Server.On(HttpMethod.Post, "/auth/refresh", body =>
        {
            refreshed.Add(JsonSerializer.Deserialize<Dictionary<string, string>>(body!)!["refresh_token"]);
            return (HttpStatusCode.OK, Harness.Tokens("a2", "r2"));
        });
        using var signIn = h.Build();
        h.Observed.FailSaveWith = () => new InvalidOperationException("the keystore is unavailable");

        Assert.Equal("a2", await signIn.GetAccessTokenAsync());
        Assert.Equal("a2", await signIn.GetAccessTokenAsync());
        h.Clock.Now += TimeSpan.FromMinutes(30);
        Assert.Equal("a2", await signIn.GetAccessTokenAsync());

        Assert.Equal(["r1"], refreshed);
        Assert.Equal("r2", (await signIn.GetSessionAsync())!.RefreshToken);
        Assert.Equal("r1", (await h.Store.LoadAsync())!.RefreshToken);
        Assert.Single(h.Changes);
    }

    [Fact]
    public async Task The_store_is_written_without_the_callers_cancellation_token_so_a_cancelled_caller_cannot_lose_rotated_tokens()
    {
        var h = new Harness();
        await h.StoreSessionAsync(TimeSpan.FromSeconds(30));
        using var cts = new CancellationTokenSource();
        var refreshed = new List<string>();
        h.Server.On(HttpMethod.Post, "/auth/refresh", body =>
        {
            refreshed.Add(JsonSerializer.Deserialize<Dictionary<string, string>>(body!)!["refresh_token"]);
            cts.Cancel(); // the caller gives up while the server is already rotating
            return (HttpStatusCode.OK, Harness.Tokens("a2", "r2"));
        });
        using var signIn = h.Build();

        Assert.Equal("a2", await signIn.GetAccessTokenAsync(cts.Token));
        Assert.Equal("a2", await signIn.GetAccessTokenAsync());

        Assert.Equal(["r1"], refreshed);
        Assert.All(h.Observed.SaveTokens, t => Assert.False(t.CanBeCanceled));
        Assert.Equal("r2", (await h.Store.LoadAsync())!.RefreshToken);
    }

    [Fact]
    public async Task A_malformed_success_answer_is_not_a_sign_out_while_the_token_is_valid()
    {
        var h = new Harness();
        await h.StoreSessionAsync(TimeSpan.FromSeconds(30));
        h.Server.On(HttpMethod.Post, "/auth/refresh", _ => (HttpStatusCode.OK, "<html>not json</html>"));
        using var signIn = h.Build();

        Assert.Equal("a1", await signIn.GetAccessTokenAsync());
        Assert.Equal("r1", (await h.Store.LoadAsync())!.RefreshToken);
    }
}
