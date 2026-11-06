using System.Net;
using System.Text.Json;
using Xunit;

namespace Vocab.Sdk.Maui.Tests;

/// <summary>Event ordering, a store that fails to forget, the events every change raises, and the refresh window.</summary>
public class FollowUpTests
{
    [Fact]
    public async Task A_sign_in_overtaken_by_a_sign_out_does_not_announce_itself_after_it()
    {
        // Sign-in replaces r1 with r9 and revokes r1 slowly; a sign-out completes meanwhile. The last event must be
        // the sign-out's null, not the sign-in's stale session.
        var h = new Harness();
        await h.StoreSessionAsync(TimeSpan.FromHours(1));
        h.Server.On(HttpMethod.Post, "/oauth/token", _ => (HttpStatusCode.OK, Harness.Tokens("a9", "r9")));
        var oldRevoke = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Server.On(HttpMethod.Post, "/oauth/revoke", async (_, body) =>
        {
            if (body == "token=r1")
                await oldRevoke.Task;
            return FakeAuthority.Json(HttpStatusCode.OK, "{}");
        });
        using var signIn = h.Build();

        var signingIn = signIn.SignInAsync();
        await Task.Delay(50);
        Assert.False(signingIn.IsCompleted);
        Assert.Equal("a9", (await signIn.GetSessionAsync())!.AccessToken);

        Assert.True(await signIn.SignOutAsync());
        Assert.Equal([null], h.Changes);

        oldRevoke.SetResult();
        await signingIn;

        Assert.Equal([null], h.Changes);
        Assert.Null(await signIn.GetSessionAsync());
    }

    [Fact]
    public async Task A_store_that_cannot_forget_the_session_does_not_stop_the_revocation()
    {
        var h = new Harness();
        await h.StoreSessionAsync(TimeSpan.FromHours(1));
        string? revokeBody = null;
        h.Server.On(HttpMethod.Post, "/oauth/revoke", body =>
        {
            revokeBody = body;
            return (HttpStatusCode.OK, "{}");
        });
        using var signIn = h.Build();
        h.Observed.FailClearWith = () => new InvalidOperationException("the keystore is unavailable");

        await Assert.ThrowsAsync<InvalidOperationException>(() => signIn.SignOutAsync());

        Assert.Equal("token=r1", revokeBody);
        Assert.Null(await signIn.GetSessionAsync());
        Assert.Equal([null], h.Changes);
    }

    [Fact]
    public async Task A_sign_in_the_store_cannot_persist_is_still_announced()
    {
        var h = new Harness();
        h.Server.On(HttpMethod.Post, "/oauth/token", _ => (HttpStatusCode.OK, Harness.Tokens("a1", "r1")));
        using var signIn = h.Build();
        h.Observed.FailSaveWith = () => new InvalidOperationException("the keystore is unavailable");

        await Assert.ThrowsAsync<InvalidOperationException>(() => signIn.SignInAsync());

        var change = Assert.Single(h.Changes);
        Assert.Equal("a1", change!.AccessToken);
    }

    [Fact]
    public async Task A_revoked_session_the_store_cannot_forget_is_still_announced_as_ended()
    {
        var h = new Harness();
        await h.StoreSessionAsync(TimeSpan.FromSeconds(30));
        h.Server.On(HttpMethod.Post, "/auth/refresh", _ => (HttpStatusCode.BadRequest, """{"error":"gone","code":"invalid_grant"}"""));
        using var signIn = h.Build();
        h.Observed.FailClearWith = () => new InvalidOperationException("the keystore is unavailable");

        Assert.Null(await signIn.GetAccessTokenAsync());

        Assert.Equal([null], h.Changes);
        Assert.Null(await signIn.GetSessionAsync());
    }

    [Fact]
    public async Task A_sign_out_cancelled_during_the_revocation_is_still_announced()
    {
        var h = new Harness();
        await h.StoreSessionAsync(TimeSpan.FromHours(1));
        using var cts = new CancellationTokenSource();
        h.Server.On(HttpMethod.Post, "/oauth/revoke", (_, _) =>
        {
            cts.Cancel();
            throw new TaskCanceledException();
        });
        using var signIn = h.Build();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => signIn.SignOutAsync(cts.Token));

        Assert.Equal([null], h.Changes);
    }

    [Fact]
    public async Task A_short_lived_token_is_used_for_half_its_life_rather_than_refreshed_on_every_call()
    {
        var h = new Harness();
        h.Server.On(HttpMethod.Post, "/oauth/token", _ => (HttpStatusCode.OK, Harness.Tokens("a1", "r1", expiresIn: 60)));
        var refreshes = 0;
        h.Server.On(HttpMethod.Post, "/auth/refresh", _ =>
        {
            refreshes++;
            return (HttpStatusCode.OK, Harness.Tokens($"a{refreshes + 1}", $"r{refreshes + 1}", expiresIn: 60));
        });
        using var signIn = h.Build();
        var session = await signIn.SignInAsync();

        Assert.Equal(TimeSpan.FromSeconds(30), session.RefreshWindow(h.SignInOptions.RefreshBefore));
        Assert.Equal("a1", await signIn.GetAccessTokenAsync());
        h.Clock.Now += TimeSpan.FromSeconds(29);
        Assert.Equal("a1", await signIn.GetAccessTokenAsync());
        Assert.Equal(0, refreshes);

        h.Clock.Now += TimeSpan.FromSeconds(2);
        Assert.Equal("a2", await signIn.GetAccessTokenAsync());
        Assert.Equal("a2", await signIn.GetAccessTokenAsync());
        Assert.Equal(1, refreshes);
    }

    [Fact]
    public async Task A_session_stored_without_an_issue_time_keeps_the_configured_window()
    {
        var h = new Harness();
        var legacy = await h.StoreSessionAsync(TimeSpan.FromSeconds(90));
        Assert.Null(legacy.IssuedAt);
        h.Server.On(HttpMethod.Post, "/auth/refresh", _ => (HttpStatusCode.OK, Harness.Tokens("a2", "r2")));
        using var signIn = h.Build();

        Assert.Equal(h.SignInOptions.RefreshBefore, legacy.RefreshWindow(h.SignInOptions.RefreshBefore));
        Assert.Equal("a2", await signIn.GetAccessTokenAsync());
    }

    [Fact]
    public void The_issue_time_round_trips_through_the_store()
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var session = VocabSession.From(new VocabTokens("a", "Bearer", 3600, "r", null, null), now);
        var json = JsonSerializer.Serialize(session);

        Assert.Equal(now, session.IssuedAt);
        Assert.Equal(session, JsonSerializer.Deserialize<VocabSession>(json));
    }
}
