using System.Net;
using System.Text.Json;
using Xunit;

namespace Vocab.Sdk.Maui.Tests;

public class SignInTests
{
    [Fact]
    public async Task Sign_in_sends_the_user_to_auth_with_pkce_redeems_the_code_as_a_public_client_and_stores_the_session()
    {
        var h = new Harness();
        string? redeemBody = null;
        h.Server.On(HttpMethod.Post, "/oauth/token", body =>
        {
            redeemBody = body;
            return (HttpStatusCode.OK, Harness.Tokens("a1", "r1"));
        });
        using var signIn = h.Build();

        var session = await signIn.SignInAsync();

        var query = FakeAuthenticator.Query(h.Authenticator.AuthorizeUrl!);
        Assert.Equal(Harness.Authority.Host, h.Authenticator.AuthorizeUrl!.Host);
        Assert.Equal("/oauth/authorize", h.Authenticator.AuthorizeUrl.AbsolutePath);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("app", query["client_id"]);
        Assert.Equal("vocabsample://callback", query["redirect_uri"]);
        Assert.Equal("openid vo.profile.basic", query["scope"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Equal(new Uri("vocabsample://callback"), h.Authenticator.CallbackUrl);

        var redeem = JsonSerializer.Deserialize<Dictionary<string, string>>(redeemBody!)!;
        Assert.Equal("authorization_code", redeem["grant_type"]);
        Assert.Equal("the-code", redeem["code"]);
        Assert.Equal("vocabsample://callback", redeem["redirect_uri"]);
        Assert.Equal(query["code_challenge"], Pkce.ChallengeFor(redeem["code_verifier"]));
        Assert.False(redeem.ContainsKey("client_secret"));

        Assert.Equal("a1", session.AccessToken);
        Assert.Equal("r1", session.RefreshToken);
        Assert.Equal(h.Clock.Now.AddHours(1), session.ExpiresAt);
        Assert.Equal(session, await h.Store.LoadAsync());
        Assert.Equal(session, await signIn.GetSessionAsync());
        Assert.True(await signIn.IsSignedInAsync());
        Assert.Equal([session], h.Changes);
    }

    [Fact]
    public async Task Each_sign_in_uses_a_fresh_state_and_verifier()
    {
        var h = new Harness();
        h.Server.On(HttpMethod.Post, "/oauth/token", _ => (HttpStatusCode.OK, Harness.Tokens("a1")));
        using var signIn = h.Build();

        await signIn.SignInAsync();
        var first = FakeAuthenticator.Query(h.Authenticator.AuthorizeUrl!);
        await signIn.SignInAsync();
        var second = FakeAuthenticator.Query(h.Authenticator.AuthorizeUrl!);

        Assert.NotEqual(first["state"], second["state"]);
        Assert.NotEqual(first["code_challenge"], second["code_challenge"]);
        Assert.True(first["state"].Length >= 32);
    }

    [Fact]
    public async Task Scopes_passed_to_sign_in_override_the_configured_ones()
    {
        var h = new Harness();
        h.Server.On(HttpMethod.Post, "/oauth/token", _ => (HttpStatusCode.OK, Harness.Tokens("a1")));
        using var signIn = h.Build();

        await signIn.SignInAsync([VocabScopes.OpenId, VocabScopes.ProfileBasic, VocabScopes.ProfileWrite]);

        Assert.Equal("openid vo.profile.basic vo.profile.write", FakeAuthenticator.Query(h.Authenticator.AuthorizeUrl!)["scope"]);
    }

    [Fact]
    public async Task A_callback_with_the_wrong_state_is_refused_before_anything_is_redeemed()
    {
        var h = new Harness { Authenticator = new FakeAuthenticator(_ => new Dictionary<string, string> { ["code"] = "c", ["state"] = "someone-elses" }) };
        using var signIn = h.Build();

        var ex = await Assert.ThrowsAsync<VocabSignInException>(() => signIn.SignInAsync());

        Assert.Equal(VocabSignInException.InvalidState, ex.Code);
        Assert.Empty(h.Server.Requests);
        Assert.Null(await signIn.GetSessionAsync());
        Assert.Empty(h.Changes);
    }

    [Fact]
    public async Task A_callback_without_state_is_refused_even_when_it_carries_an_error()
    {
        var h = new Harness { Authenticator = new FakeAuthenticator(_ => new Dictionary<string, string> { ["error"] = "access_denied" }) };
        using var signIn = h.Build();

        var ex = await Assert.ThrowsAsync<VocabSignInException>(() => signIn.SignInAsync());

        Assert.Equal(VocabSignInException.InvalidState, ex.Code);
    }

    [Fact]
    public async Task The_users_refusal_surfaces_as_access_denied_with_auths_description()
    {
        var h = new Harness
        {
            Authenticator = new FakeAuthenticator(url => new Dictionary<string, string>
            {
                ["error"] = "access_denied", ["error_description"] = "The user declined.", ["state"] = FakeAuthenticator.Query(url)["state"],
            }),
        };
        using var signIn = h.Build();

        var ex = await Assert.ThrowsAsync<VocabSignInException>(() => signIn.SignInAsync());

        Assert.True(ex.IsAccessDenied);
        Assert.Equal("The user declined.", ex.Message);
        Assert.Empty(h.Server.Requests);
    }

    [Fact]
    public async Task A_callback_with_neither_code_nor_error_is_refused()
    {
        var h = new Harness { Authenticator = new FakeAuthenticator(url => new Dictionary<string, string> { ["state"] = FakeAuthenticator.Query(url)["state"] }) };
        using var signIn = h.Build();

        var ex = await Assert.ThrowsAsync<VocabSignInException>(() => signIn.SignInAsync());

        Assert.Equal(VocabSignInException.MissingCode, ex.Code);
    }

    [Fact]
    public async Task Auth_refusing_the_code_surfaces_as_the_api_error_and_leaves_no_session()
    {
        var h = new Harness();
        h.Server.On(HttpMethod.Post, "/oauth/token", _ => (HttpStatusCode.BadRequest, """{"error":"The code was already used.","code":"invalid_grant"}"""));
        using var signIn = h.Build();

        var ex = await Assert.ThrowsAsync<VocabApiException>(() => signIn.SignInAsync());

        Assert.Equal(VocabErrorCodes.InvalidGrant, ex.Code);
        Assert.Null(await signIn.GetSessionAsync());
        Assert.Empty(h.Storage.Entries);
    }

    [Fact]
    public async Task The_user_closing_the_browser_cancels_the_sign_in()
    {
        var h = new Harness { Authenticator = new FakeAuthenticator(_ => throw new TaskCanceledException()) };
        using var signIn = h.Build();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => signIn.SignInAsync());
        Assert.Null(await signIn.GetSessionAsync());
    }

    [Fact]
    public async Task A_session_stored_by_a_previous_launch_is_picked_up_without_a_sign_in()
    {
        var h = new Harness();
        var stored = await h.StoreSessionAsync(TimeSpan.FromHours(1));
        using var signIn = h.Build();

        Assert.Equal(stored, await signIn.GetSessionAsync());
        Assert.Equal("a1", await signIn.GetAccessTokenAsync());
        Assert.Empty(h.Server.Requests);
    }

    [Fact]
    public async Task A_sign_in_the_store_cannot_persist_throws_but_stands_in_memory_for_this_launch()
    {
        var h = new Harness();
        h.Server.On(HttpMethod.Post, "/oauth/token", _ => (HttpStatusCode.OK, Harness.Tokens("a1", "r1")));
        using var signIn = h.Build();
        h.Observed.FailSaveWith = () => new InvalidOperationException("the keystore is unavailable");

        await Assert.ThrowsAsync<InvalidOperationException>(() => signIn.SignInAsync());

        Assert.Equal("a1", (await signIn.GetSessionAsync())!.AccessToken);
        Assert.Equal("a1", await signIn.GetAccessTokenAsync());
        Assert.Empty(h.Storage.Entries);
    }

    [Fact]
    public async Task Signing_in_over_an_existing_session_replaces_it_and_revokes_the_old_refresh_token()
    {
        var h = new Harness();
        await h.StoreSessionAsync(TimeSpan.FromHours(1));
        h.Server.On(HttpMethod.Post, "/oauth/token", _ => (HttpStatusCode.OK, Harness.Tokens("a9", "r9")));
        string? revokeBody = null;
        h.Server.On(HttpMethod.Post, "/oauth/revoke", body =>
        {
            revokeBody = body;
            return (HttpStatusCode.OK, "{}");
        });
        using var signIn = h.Build();

        var session = await signIn.SignInAsync();

        Assert.Equal("token=r1", revokeBody);
        Assert.Equal("r9", session.RefreshToken);
        Assert.Equal(session, await h.Store.LoadAsync());
        Assert.Equal([session], h.Changes);
    }

    [Fact]
    public async Task The_old_refresh_token_failing_to_revoke_does_not_fail_the_new_sign_in()
    {
        var h = new Harness();
        await h.StoreSessionAsync(TimeSpan.FromHours(1));
        h.Server.On(HttpMethod.Post, "/oauth/token", _ => (HttpStatusCode.OK, Harness.Tokens("a9", "r9")));
        h.Server.On(HttpMethod.Post, "/oauth/revoke", (_, _) => throw new HttpRequestException("offline"));
        using var signIn = h.Build();

        Assert.Equal("a9", (await signIn.SignInAsync()).AccessToken);
        Assert.Equal("a9", await signIn.GetAccessTokenAsync());
    }
}
