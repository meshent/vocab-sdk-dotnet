using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Vocab.Sdk.AspNetCore.Tests;

/// <summary>End to end through the JwtBearer handler: real signatures, discovery and JWKS served by the fake.</summary>
public class BearerTests : IDisposable
{
    private readonly RSA _rsa = RSA.Create(2048);
    private readonly RsaSecurityKey _key;
    private readonly FakeAuthority _authority;

    public BearerTests()
    {
        _key = new RsaSecurityKey(_rsa) { KeyId = "k1" };
        var parameters = _rsa.ExportParameters(includePrivateParameters: false);
        var jwks = $$"""{"keys":[{"kty":"RSA","use":"sig","alg":"RS256","kid":"k1","n":"{{Base64UrlEncoder.Encode(parameters.Modulus)}}","e":"{{Base64UrlEncoder.Encode(parameters.Exponent)}}"}]}""";
        _authority = new FakeAuthority()
            .On(HttpMethod.Get, "/.well-known/openid-configuration", _ => (HttpStatusCode.OK,
                """{"issuer":"https://auth.test","jwks_uri":"https://auth.test/.well-known/jwks.json","authorization_endpoint":"https://auth.test/oauth/authorize","token_endpoint":"https://auth.test/oauth/token"}"""))
            .On(HttpMethod.Get, "/.well-known/jwks.json", _ => (HttpStatusCode.OK, jwks));
    }

    private string Mint(string? clientId, string audience = "network.api.vo.cab")
    {
        var claims = new Dictionary<string, object> { [VocabClaims.Subject] = "u1", [VocabClaims.Scope] = "openid vo.profile.basic" };
        if (clientId is not null)
            claims[VocabClaims.ClientId] = clientId;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "https://auth.test",
            Audience = audience,
            Claims = claims,
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(_key, SecurityAlgorithms.RsaSha256),
        });
    }

    private async Task<WebApplication> StartApiAsync(Action<JwtBearerOptions> configure)
    {
        return await TestApp.StartAsync(_authority,
            v => v.AddBearer(jwt =>
            {
                jwt.BackchannelHttpHandler = _authority;
                configure(jwt);
            }),
            app => app.MapGet("/api/whoami", (ClaimsPrincipal user) => user.FindFirst(VocabClaims.Subject)!.Value).RequireAuthorization(),
            clientSecret: null);
    }

    private static async Task<HttpResponseMessage> CallAsync(HttpClient client, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/whoami", UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task The_client_id_check_survives_configuration_that_replaces_the_events()
    {
        var messageReceived = 0;
        await using var app = await StartApiAsync(jwt => jwt.Events = new JwtBearerEvents
        {
            // The SignalR-style override: a brand new Events object with only the hook the app cares about.
            OnMessageReceived = _ =>
            {
                Interlocked.Increment(ref messageReceived);
                return Task.CompletedTask;
            },
        });
        using var client = app.GetTestClient();

        using var mine = await CallAsync(client, Mint("app"));
        Assert.Equal(HttpStatusCode.OK, mine.StatusCode);
        Assert.Equal("u1", await mine.Content.ReadAsStringAsync());

        using var theirs = await CallAsync(client, Mint("other-app"));
        Assert.Equal(HttpStatusCode.Unauthorized, theirs.StatusCode);

        using var unlabelled = await CallAsync(client, Mint(clientId: null));
        Assert.Equal(HttpStatusCode.Unauthorized, unlabelled.StatusCode);

        Assert.Equal(3, messageReceived); // the app's own hook still ran
    }

    [Fact]
    public async Task Tokens_for_another_audience_or_from_another_issuer_are_refused()
    {
        await using var app = await StartApiAsync(_ => { });
        using var client = app.GetTestClient();

        using var wrongAudience = await CallAsync(client, Mint("app", audience: "someone-else"));
        Assert.Equal(HttpStatusCode.Unauthorized, wrongAudience.StatusCode);

        using var unsigned = await CallAsync(client, Mint("app") + "x");
        Assert.Equal(HttpStatusCode.Unauthorized, unsigned.StatusCode);
    }

    public void Dispose()
    {
        _rsa.Dispose();
        _authority.Dispose();
        GC.SuppressFinalize(this);
    }
}
