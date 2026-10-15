using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Vocab.Sdk.AspNetCore.Tests;

public class RegistrationTests
{
    private static ServiceProvider Build(Action<IServiceCollection> register)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        register(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    [Fact]
    public async Task AddSignIn_wires_cookie_plus_OpenIdConnect_with_PKCE_against_the_authority()
    {
        using var sp = Build(s => s.AddVocab(o =>
        {
            o.NetworkId = "example";
            o.ClientId = "app";
            o.ClientSecret = "s3cret";
        }).AddSignIn(signIn => signIn.Scopes.Add(VocabScopes.ProfileWrite)).AddClient());

        var oidc = sp.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get(VocabDefaults.SignInScheme);
        Assert.Equal("https://auth.vo.cab", oidc.Authority);
        Assert.Equal("app", oidc.ClientId);
        Assert.Equal("s3cret", oidc.ClientSecret);
        Assert.True(oidc.UsePkce);
        Assert.True(oidc.SaveTokens);
        Assert.False(oidc.MapInboundClaims);
        Assert.Equal("code", oidc.ResponseType);
        Assert.Equal("/signin-vocab", oidc.CallbackPath.Value);
        Assert.Equal(["openid", "vo.profile.basic", "vo.profile.write"], oidc.Scope);
        Assert.NotNull(oidc.Events.OnAuthorizationCodeReceived);

        var schemes = sp.GetRequiredService<IAuthenticationSchemeProvider>();
        Assert.Equal(VocabDefaults.CookieScheme, (await schemes.GetDefaultAuthenticateSchemeAsync())!.Name);
        Assert.Equal(VocabDefaults.SignInScheme, (await schemes.GetDefaultChallengeSchemeAsync())!.Name);
        // auth.vo.cab has no end-session endpoint: signing out must never reach the OpenID Connect handler.
        Assert.Equal(VocabDefaults.CookieScheme, (await schemes.GetDefaultSignOutSchemeAsync())!.Name);
        var cookie = sp.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(VocabDefaults.CookieScheme);
        Assert.NotNull(cookie.Events.OnSigningOut);

        using var scope = sp.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<VocabClient>();
        Assert.NotNull(client);
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<VocabAuthClient>());
    }

    [Fact]
    public void AddBearer_requires_the_network_api_audience_and_the_authority()
    {
        using var sp = Build(s => s.AddVocab(o =>
        {
            o.NetworkId = "example";
            o.ClientId = "app";
        }).AddBearer().AddClient());

        var jwt = sp.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(VocabDefaults.BearerScheme);
        Assert.Equal("https://auth.vo.cab", jwt.Authority);
        Assert.Equal("network.api.vo.cab", jwt.Audience);
        Assert.False(jwt.MapInboundClaims);
        Assert.NotEmpty(jwt.TokenHandlers);
        Assert.All(jwt.TokenHandlers, handler => Assert.IsType<MintedForThisAppTokenHandler>(handler));
    }

    [Fact]
    public void Options_from_configuration_are_validated_at_startup()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Vocab:NetworkId"] = "example",
            ["Vocab:ClientId"] = "app",
            ["Vocab:Authority"] = "https://auth.test.vo.cab",
        }).Build();

        using var sp = Build(s => s.AddVocab(configuration.GetSection("Vocab")).AddSignIn());
        var options = sp.GetRequiredService<IOptions<VocabOptions>>().Value;
        Assert.Equal(new Uri("https://auth.test.vo.cab"), options.Authority);
        Assert.Equal(new Uri("https://example.api.vo.cab/"), options.ApiBaseAddress);

        var missing = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Vocab:NetworkId"] = "example" }).Build();
        using var broken = Build(s => s.AddVocab(missing.GetSection("Vocab")).AddSignIn());
        var ex = Assert.Throws<InvalidOperationException>(() => broken.GetRequiredService<IOptions<VocabOptions>>().Value);
        Assert.Contains("ClientId", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_bearer_token_minted_for_another_app_is_refused()
    {
        var mine = new ClaimsPrincipal(new ClaimsIdentity([new Claim(VocabClaims.ClientId, "app")]));
        var theirs = new ClaimsPrincipal(new ClaimsIdentity([new Claim(VocabClaims.ClientId, "other-app")]));
        var none = new ClaimsPrincipal(new ClaimsIdentity());

        Assert.True(VocabBearerValidation.IsMintedFor(mine, "app"));
        Assert.False(VocabBearerValidation.IsMintedFor(theirs, "app"));
        Assert.False(VocabBearerValidation.IsMintedFor(none, "app"));
    }

    [Fact]
    public void GetVocab_reads_the_identity_off_the_claims()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(VocabClaims.Subject, "u1"),
            new Claim(VocabClaims.PreferredUsername, "jane"),
            new Claim(VocabClaims.ProfileId, "p1"),
            new Claim(VocabClaims.NetworkId, "example"),
            new Claim(VocabClaims.Handle, "jane_doe"),
            new Claim("name", "Jane Doe"),
            new Claim(VocabClaims.Roles, "member"),
            new Claim(VocabClaims.Roles, "editor"),
            new Claim(VocabClaims.Scope, "openid vo.profile.basic"),
            new Claim(VocabClaims.Permissions, "net:pages.read net:pages.write"),
        ]));

        var vocab = principal.GetVocab();

        Assert.NotNull(vocab);
        Assert.Equal("u1", vocab.UserId);
        Assert.Equal("jane", vocab.Username);
        Assert.True(vocab.HasProfile);
        Assert.Equal("p1", vocab.ProfileId);
        Assert.Equal("jane_doe", vocab.Handle);
        Assert.Equal("Jane Doe", vocab.DisplayName);
        Assert.Equal(["member", "editor"], vocab.Roles);
        Assert.Equal(["openid", "vo.profile.basic"], vocab.Scopes);
        Assert.True(principal.HasVocabScope(VocabScopes.ProfileBasic));
        Assert.False(principal.HasVocabScope(VocabScopes.ProfileWrite));
        Assert.True(principal.HasVocabPermission("net:pages.write"));

        Assert.Null(new ClaimsPrincipal(new ClaimsIdentity()).GetVocab());
        Assert.False(new ClaimsPrincipal(new ClaimsIdentity([new Claim(VocabClaims.Subject, "u2")])).GetVocab()!.HasProfile);
    }
}
