using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Vocab.Sdk.AspNetCore.Tests;

/// <summary>
/// <c>AddBearer(asDefaultScheme: false)</c>: an API that already defaults to its own scheme keeps it, and accepts
/// vo.cab tokens only on endpoints that name <see cref="VocabDefaults.BearerScheme"/>.
/// </summary>
public class BearerBesideDefaultTests : IDisposable
{
    private const string OwnScheme = "Own";
    private const string OwnIssuer = "https://own.test";
    private const string OwnAudience = "own-api";

    private readonly RSA _rsa = RSA.Create(2048);
    private readonly RsaSecurityKey _vocabKey;
    private readonly SymmetricSecurityKey _ownKey = new(RandomNumberGenerator.GetBytes(32));
    private readonly FakeAuthority _authority;

    public BearerBesideDefaultTests()
    {
        _vocabKey = new RsaSecurityKey(_rsa) { KeyId = "k1" };
        var parameters = _rsa.ExportParameters(includePrivateParameters: false);
        var jwks = $$"""{"keys":[{"kty":"RSA","use":"sig","alg":"RS256","kid":"k1","n":"{{Base64UrlEncoder.Encode(parameters.Modulus)}}","e":"{{Base64UrlEncoder.Encode(parameters.Exponent)}}"}]}""";
        _authority = new FakeAuthority()
            .On(HttpMethod.Get, "/.well-known/openid-configuration", _ => (HttpStatusCode.OK,
                """{"issuer":"https://auth.test","jwks_uri":"https://auth.test/.well-known/jwks.json","authorization_endpoint":"https://auth.test/oauth/authorize","token_endpoint":"https://auth.test/oauth/token"}"""))
            .On(HttpMethod.Get, "/.well-known/jwks.json", _ => (HttpStatusCode.OK, jwks));
    }

    private string MintVocab(string clientId) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "https://auth.test",
            Audience = "network.api.vo.cab",
            Claims = new Dictionary<string, object> { [VocabClaims.Subject] = "vocab-user", [VocabClaims.ClientId] = clientId },
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(_vocabKey, SecurityAlgorithms.RsaSha256),
        });

    /// <summary>A token of the API's own HS256 scheme, as an app that predates vo.cab sign-in would issue.</summary>
    private string MintOwn() =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = OwnIssuer,
            Audience = OwnAudience,
            Claims = new Dictionary<string, object> { ["sub"] = "own-user" },
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(_ownKey, SecurityAlgorithms.HmacSha256),
        });

    private void AddOwnScheme(IServiceCollection services) =>
        services.AddAuthentication(OwnScheme).AddJwtBearer(OwnScheme, jwt =>
        {
            jwt.MapInboundClaims = false;
            jwt.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer = OwnIssuer,
                ValidAudience = OwnAudience,
                IssuerSigningKey = _ownKey,
            };
        });

    private Task<WebApplication> StartAsync(bool ownSchemeFirst) =>
        TestApp.StartAsync(_authority,
            v =>
            {
                if (ownSchemeFirst)
                    AddOwnScheme(v.Services);
                v.AddBearer(asDefaultScheme: false, jwt => jwt.BackchannelHttpHandler = _authority);
                if (!ownSchemeFirst)
                    AddOwnScheme(v.Services);
                return v;
            },
            app =>
            {
                static string Sub(ClaimsPrincipal user) => user.FindFirst("sub")!.Value;
                // No scheme named: the API's own default decides.
                app.MapGet("/api/own", Sub).RequireAuthorization();
                // Opted into vo.cab tokens.
                app.MapGet("/api/vocab", Sub).RequireAuthorization(policy => policy
                    .AddAuthenticationSchemes(VocabDefaults.BearerScheme)
                    .RequireAuthenticatedUser());
            },
            clientSecret: null);

    private static async Task<(HttpStatusCode Status, string Body)> CallAsync(HttpClient client, string path, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_apps_own_default_scheme_is_kept_whichever_is_registered_first(bool ownSchemeFirst)
    {
        await using var app = await StartAsync(ownSchemeFirst);

        var options = app.Services.GetRequiredService<IOptions<AuthenticationOptions>>().Value;
        Assert.Equal(OwnScheme, options.DefaultScheme);
        Assert.Null(options.DefaultAuthenticateScheme);
        Assert.Null(options.DefaultChallengeScheme);
        Assert.Null(options.DefaultForbidScheme);

        var schemes = app.Services.GetRequiredService<IAuthenticationSchemeProvider>();
        Assert.NotNull(await schemes.GetSchemeAsync(VocabDefaults.BearerScheme));
        Assert.Equal(OwnScheme, (await schemes.GetDefaultAuthenticateSchemeAsync())!.Name);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_own_scheme_authenticates_its_tokens_and_the_Vocab_scheme_accepts_only_this_apps_vocab_tokens(bool ownSchemeFirst)
    {
        await using var app = await StartAsync(ownSchemeFirst);
        using var client = app.GetTestClient();

        // Endpoints on the default scheme: the API's own tokens, as before.
        Assert.Equal((HttpStatusCode.OK, "own-user"), await CallAsync(client, "/api/own", MintOwn()));
        Assert.Equal(HttpStatusCode.Unauthorized, (await CallAsync(client, "/api/own", MintVocab("app"))).Status);

        // Endpoints that name the Vocab scheme: vo.cab tokens minted for this app, and only those.
        Assert.Equal((HttpStatusCode.OK, "vocab-user"), await CallAsync(client, "/api/vocab", MintVocab("app")));
        Assert.Equal(HttpStatusCode.Unauthorized, (await CallAsync(client, "/api/vocab", MintVocab("other-app"))).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await CallAsync(client, "/api/vocab", MintOwn())).Status);
    }

    [Fact]
    public void The_original_AddBearer_still_makes_the_Vocab_scheme_the_default()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddVocab(o =>
        {
            o.NetworkId = "example";
            o.ClientId = "app";
        }).AddBearer();
        using var sp = services.BuildServiceProvider();

        Assert.Equal(VocabDefaults.BearerScheme, sp.GetRequiredService<IOptions<AuthenticationOptions>>().Value.DefaultScheme);
    }

    [Fact]
    public void Without_the_default_the_legacy_security_token_validators_path_is_still_refused()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddVocab(o =>
        {
            o.NetworkId = "example";
            o.ClientId = "app";
        }).AddBearer(asDefaultScheme: false, jwt => jwt.UseSecurityTokenValidators = true);
        using var sp = services.BuildServiceProvider();

        Assert.Null(sp.GetRequiredService<IOptions<AuthenticationOptions>>().Value.DefaultScheme);
        var ex = Assert.Throws<InvalidOperationException>(() =>
            sp.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(VocabDefaults.BearerScheme));
        Assert.Contains("UseSecurityTokenValidators", ex.Message, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        _rsa.Dispose();
        _authority.Dispose();
        GC.SuppressFinalize(this);
    }
}
