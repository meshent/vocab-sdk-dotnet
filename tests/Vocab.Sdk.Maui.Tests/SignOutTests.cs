using System.Net;
using Xunit;

namespace Vocab.Sdk.Maui.Tests;

public class SignOutTests
{
    [Fact]
    public async Task Sign_out_forgets_the_session_and_revokes_the_refresh_token()
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

        Assert.True(await signIn.SignOutAsync());

        Assert.Equal("token=r1", revokeBody);
        Assert.Null(await signIn.GetSessionAsync());
        Assert.Null(await signIn.GetAccessTokenAsync());
        Assert.Empty(h.Storage.Entries);
        Assert.Equal([null], h.Changes);
    }

    [Fact]
    public async Task The_app_is_signed_out_even_when_auth_cannot_be_reached_and_says_so()
    {
        var h = new Harness();
        await h.StoreSessionAsync(TimeSpan.FromHours(1));
        h.Server.On(HttpMethod.Post, "/oauth/revoke", (_, _) => throw new HttpRequestException("offline"));
        using var signIn = h.Build();

        Assert.False(await signIn.SignOutAsync());

        Assert.Null(await signIn.GetSessionAsync());
        Assert.Empty(h.Storage.Entries);
    }

    [Fact]
    public async Task Auth_refusing_the_revocation_is_reported_the_same_way()
    {
        var h = new Harness();
        await h.StoreSessionAsync(TimeSpan.FromHours(1));
        h.Server.On(HttpMethod.Post, "/oauth/revoke", _ => (HttpStatusCode.BadRequest, """{"error":"nope","code":"invalid_request"}"""));
        using var signIn = h.Build();

        Assert.False(await signIn.SignOutAsync());
        Assert.Empty(h.Storage.Entries);
    }

    [Fact]
    public async Task Signing_out_without_a_session_or_without_a_refresh_token_calls_nothing()
    {
        var h = new Harness();
        using var signedOut = h.Build();
        Assert.True(await signedOut.SignOutAsync());
        Assert.Empty(h.Changes);

        await h.StoreSessionAsync(TimeSpan.FromHours(1), refreshToken: null);
        using var noRefresh = h.Build();
        Assert.True(await noRefresh.SignOutAsync());

        Assert.Empty(h.Server.Requests);
        Assert.Empty(h.Storage.Entries);
    }

    [Fact]
    public async Task Signing_in_again_after_sign_out_starts_a_new_session()
    {
        var h = new Harness();
        await h.StoreSessionAsync(TimeSpan.FromHours(1));
        h.Server.On(HttpMethod.Post, "/oauth/revoke", _ => (HttpStatusCode.OK, "{}"));
        h.Server.On(HttpMethod.Post, "/oauth/token", _ => (HttpStatusCode.OK, Harness.Tokens("a9", "r9")));
        using var signIn = h.Build();

        await signIn.SignOutAsync();
        var session = await signIn.SignInAsync();

        Assert.Equal("a9", await signIn.GetAccessTokenAsync());
        Assert.Equal(session, await h.Store.LoadAsync());
        Assert.Equal([null, session], h.Changes);
    }

    [Fact]
    public async Task A_revocation_that_times_out_is_reported_as_false_not_thrown()
    {
        var h = new Harness();
        await h.StoreSessionAsync(TimeSpan.FromHours(1));
        h.Server.On(HttpMethod.Post, "/oauth/revoke", (_, _) => throw new TaskCanceledException("timed out", new TimeoutException()));
        using var signIn = h.Build();

        Assert.False(await signIn.SignOutAsync());
        Assert.Empty(h.Storage.Entries);
    }

    [Fact]
    public async Task The_callers_own_cancellation_is_still_thrown()
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
        Assert.Empty(h.Storage.Entries);
    }

    [Fact]
    public async Task A_throwing_session_changed_handler_does_not_skip_the_revocation()
    {
        var h = new Harness();
        await h.StoreSessionAsync(TimeSpan.FromHours(1));
        var revoked = false;
        h.Server.On(HttpMethod.Post, "/oauth/revoke", _ =>
        {
            revoked = true;
            return (HttpStatusCode.OK, "{}");
        });
        using var signIn = h.Build();
        signIn.SessionChanged += (_, _) => throw new InvalidOperationException("ui gone");

        await Assert.ThrowsAsync<InvalidOperationException>(() => signIn.SignOutAsync());

        Assert.True(revoked);
        Assert.Empty(h.Storage.Entries);
        Assert.Null(await signIn.GetSessionAsync());
    }
}
