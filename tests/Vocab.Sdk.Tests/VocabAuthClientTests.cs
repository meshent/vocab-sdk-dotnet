using System.Net;
using System.Text.Json;
using Xunit;

namespace Vocab.Sdk.Tests;

public class VocabAuthClientTests
{
    private static readonly VocabOptions Confidential = new() { NetworkId = "example", ClientId = "app", ClientSecret = "s3cret" };
    private static readonly VocabOptions Public = new() { NetworkId = "example", ClientId = "mobile" };

    private static VocabAuthClient Over(FakeApi api, VocabOptions options) =>
        new(api.Client("https://auth.vo.cab/"), options);

    [Fact]
    public void Pkce_matches_the_RFC_7636_example()
    {
        // RFC 7636 appendix B.
        Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", Pkce.ChallengeFor("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));

        var (verifier, challenge) = Pkce.Create();
        Assert.InRange(verifier.Length, 43, 128);
        Assert.Equal(Pkce.ChallengeFor(verifier), challenge);
    }

    [Fact]
    public void The_authorize_url_carries_the_code_flow_with_PKCE()
    {
        var url = Over(new FakeApi(), Public).BuildAuthorizeUrl("https://app.example/cb", "st", "chal", nonce: "n1");

        Assert.Equal("https", url.Scheme);
        Assert.Equal("auth.vo.cab", url.Host);
        Assert.Equal("/oauth/authorize", url.AbsolutePath);
        var query = url.Query.TrimStart('?').Split('&').Select(p => p.Split('=', 2)).ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("mobile", query["client_id"]);
        Assert.Equal("https://app.example/cb", query["redirect_uri"]);
        Assert.Equal("openid vo.profile.basic", query["scope"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Equal("chal", query["code_challenge"]);
        Assert.Equal("n1", query["nonce"]);
    }

    [Fact]
    public async Task Redeeming_a_code_posts_JSON_to_the_token_endpoint_with_the_secret_only_for_a_confidential_app()
    {
        var api = new FakeApi()
            .Answer(HttpStatusCode.OK, """{"access_token":"at","token_type":"Bearer","expires_in":900,"refresh_token":"rt","id_token":"idt","scope":"openid vo.profile.basic"}""")
            .Answer(HttpStatusCode.OK, """{"access_token":"at2","token_type":"Bearer","expires_in":900}""");

        var tokens = await Over(api, Confidential).RedeemCodeAsync("c0de", "https://app.example/cb", "verifier");
        Assert.Equal("at", tokens.AccessToken);
        Assert.Equal("rt", tokens.RefreshToken);
        Assert.Equal(900, tokens.ExpiresIn);

        var (request, body) = api.Requests[0];
        Assert.Equal("https://auth.vo.cab/oauth/token", request.RequestUri!.ToString());
        Assert.Equal("application/json", FakeApi.ContentTypeOf(request)!.MediaType);
        var sent = JsonSerializer.Deserialize<Dictionary<string, string>>(body!)!;
        Assert.Equal("authorization_code", sent["grant_type"]);
        Assert.Equal("c0de", sent["code"]);
        Assert.Equal("verifier", sent["code_verifier"]);
        Assert.Equal("app", sent["client_id"]);
        Assert.Equal("s3cret", sent["client_secret"]);

        await Over(api, Public).RedeemCodeAsync("c0de", "https://app.example/cb", "verifier");
        var publicBody = JsonSerializer.Deserialize<Dictionary<string, string>>(api.Requests[1].Body!)!;
        Assert.False(publicBody.ContainsKey("client_secret"));   // a stray secret is refused by auth.vo.cab
    }

    [Fact]
    public async Task Refresh_posts_to_auth_refresh_and_a_failure_is_a_VocabApiException()
    {
        var api = new FakeApi()
            .Answer(HttpStatusCode.OK, """{"access_token":"at2","token_type":"Bearer","expires_in":900,"refresh_token":"rt2"}""")
            .Answer(HttpStatusCode.Unauthorized, """{"error":"invalid_grant","error_description":"The refresh token is not valid."}""");
        var auth = Over(api, Confidential);

        var tokens = await auth.RefreshAsync("rt");
        Assert.Equal("rt2", tokens.RefreshToken);
        Assert.Equal("https://auth.vo.cab/auth/refresh", api.Requests[0].Request.RequestUri!.ToString());
        var sent = JsonSerializer.Deserialize<Dictionary<string, string>>(api.Requests[0].Body!)!;
        Assert.Equal("rt", sent["refresh_token"]);
        Assert.Equal("s3cret", sent["client_secret"]);

        var ex = await Assert.ThrowsAsync<VocabApiException>(() => auth.RefreshAsync("rt"));
        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
    }

    [Fact]
    public async Task The_profile_endpoint_maps_claims_and_reads_absent_or_empty_as_null()
    {
        var api = new FakeApi()
            .Answer(HttpStatusCode.OK, """{"vo_profile_id":"p1","vo_network_id":"example","vo_handle":"jane","name":"Jane","vo_roles":["member"],"email":"j@example.com","email_verified":true}""")
            .Answer(HttpStatusCode.NotFound)
            .Answer(HttpStatusCode.OK, "{}");
        var auth = Over(api, Public);

        var profile = await auth.GetProfileAsync("at");
        Assert.NotNull(profile);
        Assert.Equal("jane", profile.Handle);
        Assert.Equal(["member"], profile.Roles);
        Assert.True(profile.EmailVerified);
        Assert.Equal("at", FakeApi.BearerOf(api.Requests[0].Request));

        Assert.Null(await auth.GetProfileAsync("at"));   // no profile in the network
        Assert.Null(await auth.GetProfileAsync("at"));   // no profile scope: empty object
    }

    [Fact]
    public void Options_refuse_an_unidentifiable_app()
    {
        Assert.Throws<InvalidOperationException>(() => new VocabOptions { ClientId = "x" }.Validate());
        Assert.Throws<InvalidOperationException>(() => new VocabOptions { NetworkId = "example" }.Validate());
        Assert.Throws<InvalidOperationException>(() => new VocabOptions { NetworkId = "example.vo.cab", ClientId = "x" }.Validate());
        Assert.Equal(new Uri("https://example.api.vo.cab/"), Public.ApiBaseAddress);
        Assert.Equal(new Uri("http://example.api.localhost/"), new VocabOptions { NetworkId = "example", ClientId = "x", ApiHostSuffix = "api.localhost" }.ApiBaseAddress);
    }
}
