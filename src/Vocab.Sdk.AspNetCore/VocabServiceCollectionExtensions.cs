using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Vocab.Sdk.AspNetCore;

/// <summary>Continues the <c>AddVocab</c> registration: pick the pieces your app needs.</summary>
public interface IVocabBuilder
{
    /// <summary>The service collection being configured.</summary>
    IServiceCollection Services { get; }
}

/// <summary>Registers the Vocab SDK with ASP.NET Core.</summary>
public static class VocabServiceCollectionExtensions
{
    /// <summary>Registers the SDK with options read from configuration (for example the <c>Vocab</c> section of appsettings).</summary>
    public static IVocabBuilder AddVocab(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return services.AddVocab(configuration.Bind);
    }

    /// <summary>Registers the SDK with options set in code.</summary>
    public static IVocabBuilder AddVocab(this IServiceCollection services, Action<VocabOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<VocabOptions>()
            .Configure(configure)
            .Validate(options =>
            {
                options.Validate();
                return true;
            })
            .ValidateOnStart();

        services.AddHttpClient(VocabDefaults.AuthHttpClient, (sp, http) =>
        {
            var options = sp.GetRequiredService<IOptions<VocabOptions>>().Value;
            http.BaseAddress = options.Authority;
        });
        services.TryAddTransient(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            var options = sp.GetRequiredService<IOptions<VocabOptions>>().Value;
            return new VocabAuthClient(factory.CreateClient(VocabDefaults.AuthHttpClient), options);
        });

        return new VocabBuilder(services);
    }

    /// <summary>
    /// Sign in with vo.cab for a server-rendered app: a cookie session (the default scheme) and OpenID Connect with
    /// PKCE against auth.vo.cab (the challenge scheme). Tokens are kept in the session and refreshed before they
    /// expire. Users with no profile in your network are offered one by auth.vo.cab during sign-in.
    /// </summary>
    /// <remarks>
    /// Signing out (<c>SignOutAsync()</c> with no scheme, or the cookie scheme) clears the session and revokes its
    /// refresh token at auth.vo.cab. The user stays signed in to vo.cab itself, as with any "sign in with" provider:
    /// auth.vo.cab has no end-session endpoint, so never sign out of the OpenID Connect scheme.
    /// </remarks>
    public static IVocabBuilder AddSignIn(this IVocabBuilder builder, Action<VocabSignInOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var signIn = new VocabSignInOptions();
        configure?.Invoke(signIn);
        builder.Services.TryAddSingleton(signIn);

        builder.Services.AddHttpContextAccessor();
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.TryAddSingleton<VocabSessionRefresher>();
        // Registered before AddOpenIdConnect so your own configuration of the scheme, if any, runs after it.
        builder.Services.AddSingleton<IConfigureOptions<OpenIdConnectOptions>, ConfigureOpenIdConnect>();
        builder.Services.AddAuthentication(options =>
            {
                options.DefaultScheme = VocabDefaults.CookieScheme;
                options.DefaultChallengeScheme = VocabDefaults.SignInScheme;
                options.DefaultSignOutScheme = VocabDefaults.CookieScheme;
            })
            .AddCookie(VocabDefaults.CookieScheme, cookie =>
            {
                cookie.Cookie.Name = ".Vocab.Session";
                cookie.Cookie.HttpOnly = true;
                cookie.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax;
                cookie.Cookie.SecurePolicy = Microsoft.AspNetCore.Http.CookieSecurePolicy.Always;
                cookie.ExpireTimeSpan = signIn.SessionLifetime;
                cookie.SlidingExpiration = true;
                cookie.Events.OnValidatePrincipal = RefreshTokensIfNeededAsync;
                cookie.Events.OnSigningOut = RevokeRefreshTokenAsync;
            })
            .AddOpenIdConnect(VocabDefaults.SignInScheme, "vo.cab", oidc =>
            {
                // Authority, ClientId and ClientSecret are applied from VocabOptions in ConfigureOpenIdConnect
                // below, so a value bound from configuration is honoured.
                oidc.SignInScheme = VocabDefaults.CookieScheme;
                oidc.ResponseType = OpenIdConnectResponseType.Code;
                oidc.ResponseMode = OpenIdConnectResponseMode.Query;
                oidc.UsePkce = true;
                oidc.SaveTokens = true;
                oidc.MapInboundClaims = false;
                oidc.GetClaimsFromUserInfoEndpoint = false;
                oidc.CallbackPath = signIn.CallbackPath;
                oidc.Scope.Clear();
                foreach (var scope in signIn.Scopes)
                    oidc.Scope.Add(scope);
                oidc.TokenValidationParameters.NameClaimType = VocabClaims.PreferredUsername;
                oidc.TokenValidationParameters.RoleClaimType = VocabClaims.Roles;

                // auth.vo.cab's token endpoint takes a JSON body, so the SDK redeems the code itself and hands the
                // handler the result; everything else (state, nonce, id token validation) stays the handler's.
                oidc.Events.OnAuthorizationCodeReceived = RedeemCodeAsync;
                oidc.Events.OnTokenValidated = LoadProfileClaimsAsync;
            });

        return builder;
    }

    /// <summary>
    /// Accept vo.cab access tokens on your own API: tokens minted for your app (and only yours) by auth.vo.cab,
    /// validated against its published keys. Registers <see cref="VocabDefaults.BearerScheme"/> as the default scheme.
    /// </summary>
    /// <remarks>
    /// Every network app's token carries the same audience, so the <c>client_id</c> check is what keeps another
    /// app's token out of your API. It is applied inside token validation itself (<see cref="JwtBearerOptions.TokenHandlers"/>),
    /// after <paramref name="configure"/> has run, so neither <see cref="JwtBearerOptions.Events"/> nor
    /// <c>JwtBearerOptions.EventsType</c> can switch it off. The legacy
    /// <see cref="JwtBearerOptions.UseSecurityTokenValidators"/> path is refused at startup.
    /// </remarks>
    public static IVocabBuilder AddBearer(this IVocabBuilder builder, Action<JwtBearerOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddHttpContextAccessor();
        // Registered before AddJwtBearer so `configure` runs after it; the client_id check is a PostConfigure that
        // wraps the token handlers `configure` left behind, so it is in force whatever `configure` did.
        builder.Services.AddSingleton<IConfigureOptions<JwtBearerOptions>, ConfigureJwtBearer>();
        builder.Services.AddSingleton<IPostConfigureOptions<JwtBearerOptions>, RequireMintedForThisApp>();
        builder.Services.AddAuthentication(VocabDefaults.BearerScheme)
            .AddJwtBearer(VocabDefaults.BearerScheme, jwt =>
            {
                jwt.MapInboundClaims = false;
                jwt.TokenValidationParameters.NameClaimType = VocabClaims.Subject;
                jwt.TokenValidationParameters.RoleClaimType = VocabClaims.Roles;
                configure?.Invoke(jwt);
            });
        return builder;
    }

    /// <summary>
    /// A <see cref="VocabClient"/> for your network's API, with the current user's access token supplied from the
    /// session (after <c>AddSignIn</c>) or from the bearer token the caller presented (after <c>AddBearer</c>).
    /// </summary>
    public static IVocabBuilder AddClient(this IVocabBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddHttpContextAccessor();
        builder.Services.TryAddScoped<IVocabTokenProvider, HttpContextTokenProvider>();
        builder.Services.AddHttpClient<VocabClient>(VocabDefaults.ApiHttpClient, (sp, http) =>
        {
            var options = sp.GetRequiredService<IOptions<VocabOptions>>().Value;
            http.BaseAddress = options.ApiBaseAddress;
        });
        return builder;
    }

    // ── sign-in events ───────────────────────────────────────────────────────────────────────────────────

    private static async Task RedeemCodeAsync(AuthorizationCodeReceivedContext context)
    {
        var auth = context.HttpContext.RequestServices.GetRequiredService<VocabAuthClient>();
        var redirectUri = context.TokenEndpointRequest?.RedirectUri
                          ?? throw new InvalidOperationException("The OpenID Connect handler supplied no redirect URI for the code exchange.");
        var codeVerifier = context.TokenEndpointRequest.GetParameter("code_verifier")
                           ?? throw new InvalidOperationException("PKCE is required: the OpenID Connect handler supplied no code verifier.");

        VocabTokens tokens;
        try
        {
            tokens = await auth.RedeemCodeAsync(context.ProtocolMessage.Code, redirectUri, codeVerifier, context.HttpContext.RequestAborted);
        }
        catch (VocabApiException ex)
        {
            context.Fail(ex);
            return;
        }

        context.HandleCodeRedemption(new OpenIdConnectMessage
        {
            AccessToken = tokens.AccessToken,
            IdToken = tokens.IdToken,
            RefreshToken = tokens.RefreshToken,
            TokenType = tokens.TokenType,
            ExpiresIn = tokens.ExpiresIn.ToString(CultureInfo.InvariantCulture),
            Scope = tokens.Scope,
        });
    }

    private static async Task LoadProfileClaimsAsync(Microsoft.AspNetCore.Authentication.OpenIdConnect.TokenValidatedContext context)
    {
        var signIn = context.HttpContext.RequestServices.GetRequiredService<VocabSignInOptions>();
        if (!signIn.LoadProfileClaims || context.Principal?.Identity is not ClaimsIdentity identity)
            return;
        if (context.TokenEndpointResponse?.AccessToken is not { Length: > 0 } accessToken)
            return;

        var auth = context.HttpContext.RequestServices.GetRequiredService<VocabAuthClient>();
        VocabProfile? profile;
        try
        {
            profile = await auth.GetProfileAsync(accessToken, context.HttpContext.RequestAborted);
        }
        catch (VocabApiException ex)
        {
            // The sign-in itself succeeded; the profile is an enrichment. Log and carry on with the id token's claims.
            context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger(typeof(VocabServiceCollectionExtensions))
                .LogWarning(ex, "Signed in, but the vo.cab profile could not be read ({Code}).", ex.Code);
            return;
        }
        if (profile is null)
            return;

        AddIfMissing(identity, VocabClaims.ProfileId, profile.ProfileId);
        AddIfMissing(identity, VocabClaims.NetworkId, profile.NetworkId);
        AddIfMissing(identity, VocabClaims.Handle, profile.Handle);
        AddIfMissing(identity, "name", profile.DisplayName);
        AddIfMissing(identity, VocabClaims.AvatarUrl, profile.AvatarUrl);
        AddIfMissing(identity, "email", profile.Email);
        AddIfMissing(identity, VocabClaims.Phone, profile.Phone);
        if (profile.Roles is { Count: > 0 } && !identity.HasClaim(c => c.Type == VocabClaims.Roles))
            foreach (var role in profile.Roles)
                identity.AddClaim(new Claim(VocabClaims.Roles, role));

        // The access token's scopes and permissions, so User.GetVocab() can answer HasProfile/Scopes/Permissions.
        if (context.TokenEndpointResponse.Scope is { Length: > 0 } scope)
            AddIfMissing(identity, VocabClaims.Scope, scope);
    }

    private static void AddIfMissing(ClaimsIdentity identity, string type, string? value)
    {
        if (!string.IsNullOrEmpty(value) && !identity.HasClaim(c => c.Type == type))
            identity.AddClaim(new Claim(type, value));
    }

    // ── session refresh ──────────────────────────────────────────────────────────────────────────────────

    private static async Task RefreshTokensIfNeededAsync(CookieValidatePrincipalContext context)
    {
        var services = context.HttpContext.RequestServices;
        var signIn = services.GetRequiredService<VocabSignInOptions>();
        var expiresAt = context.Properties.GetTokenValue("expires_at");
        var refreshToken = context.Properties.GetTokenValue("refresh_token");
        if (string.IsNullOrEmpty(refreshToken) || string.IsNullOrEmpty(expiresAt))
            return;
        if (!DateTimeOffset.TryParse(expiresAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var expiry))
            return;
        var time = services.GetRequiredService<TimeProvider>();
        if (expiry - time.GetUtcNow() > signIn.RefreshBefore)
            return;

        var auth = services.GetRequiredService<VocabAuthClient>();
        var refresher = services.GetRequiredService<VocabSessionRefresher>();
        try
        {
            // One refresh per refresh token, shared by every concurrent request that carries this session.
            var tokens = await refresher.RefreshAsync(refreshToken, auth);
            context.Properties.UpdateTokenValue("access_token", tokens.AccessToken);
            if (!string.IsNullOrEmpty(tokens.RefreshToken))
                context.Properties.UpdateTokenValue("refresh_token", tokens.RefreshToken);
            if (!string.IsNullOrEmpty(tokens.IdToken))
                context.Properties.UpdateTokenValue("id_token", tokens.IdToken);
            context.Properties.UpdateTokenValue("expires_at",
                time.GetUtcNow().AddSeconds(tokens.ExpiresIn).ToString("o", CultureInfo.InvariantCulture));
            context.ShouldRenew = true;
        }
        catch (VocabApiException ex)
        {
            // The refresh token is gone (revoked, expired, signed out everywhere): the session is over.
            Logger(services).LogInformation(ex, "The vo.cab session could not be refreshed ({Code}); signing the user out.", ex.Code);
            context.RejectPrincipal();
            context.HttpContext.Items[SessionEndedKey] = true;
            await context.HttpContext.SignOutAsync(VocabDefaults.CookieScheme);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // auth.vo.cab could not be reached, or took too long. The access token is still valid for a little while,
            // so the request proceeds on it and the next request tries again.
            Logger(services).LogWarning(ex, "auth.vo.cab could not be reached to refresh the session; keeping the current token.");
        }
    }

    private static async Task RevokeRefreshTokenAsync(CookieSigningOutContext context)
    {
        var http = context.HttpContext;
        // Signed out by RefreshTokensIfNeededAsync because the refresh token was refused: nothing to revoke, and
        // authenticating here would re-enter the cookie handler's own authenticate (an infinite loop).
        if (http.Items.ContainsKey(SessionEndedKey))
            return;

        var services = http.RequestServices;
        var refreshToken = await SessionRefreshTokenAsync(http);
        if (string.IsNullOrEmpty(refreshToken))
            return;

        try
        {
            await services.GetRequiredService<VocabAuthClient>().RevokeAsync(refreshToken, http.RequestAborted);
        }
        catch (Exception ex) when (ex is VocabApiException or HttpRequestException)
        {
            // The cookie is cleared regardless; the refresh token then expires on its own.
            Logger(services).LogWarning(ex, "The vo.cab refresh token could not be revoked at sign-out.");
        }
    }

    /// <summary>The session's refresh token: from the result the authentication middleware already produced when it is this cookie's, else a fresh authenticate.</summary>
    private static async Task<string?> SessionRefreshTokenAsync(HttpContext http)
    {
        var authenticated = http.Features.Get<IAuthenticateResultFeature>()?.AuthenticateResult;
        if (authenticated?.Ticket?.AuthenticationScheme == VocabDefaults.CookieScheme)
            return authenticated.Properties?.GetTokenValue("refresh_token");
        var result = await http.AuthenticateAsync(VocabDefaults.CookieScheme);
        return result.Properties?.GetTokenValue("refresh_token");
    }

    /// <summary>Set on the request when the session was ended because auth.vo.cab refused its refresh token.</summary>
    private const string SessionEndedKey = "Vocab.Sdk.SessionEnded";

    private static ILogger Logger(IServiceProvider services) =>
        services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(VocabServiceCollectionExtensions));

    // ── options plumbing ─────────────────────────────────────────────────────────────────────────────────

    private sealed class VocabBuilder(IServiceCollection services) : IVocabBuilder
    {
        public IServiceCollection Services { get; } = services;
    }

    private sealed class ConfigureOpenIdConnect(IOptions<VocabOptions> vocab) : IConfigureNamedOptions<OpenIdConnectOptions>
    {
        public void Configure(string? name, OpenIdConnectOptions options)
        {
            if (name != VocabDefaults.SignInScheme)
                return;
            var value = vocab.Value;
            options.Authority = value.Authority.ToString().TrimEnd('/');
            options.ClientId = value.ClientId;
            options.ClientSecret = value.ClientSecret;
            options.RequireHttpsMetadata = value.Authority.Scheme == Uri.UriSchemeHttps;
        }

        public void Configure(OpenIdConnectOptions options) => Configure(Options.DefaultName, options);
    }

    private sealed class RequireMintedForThisApp(IOptions<VocabOptions> vocab) : IPostConfigureOptions<JwtBearerOptions>
    {
        public void PostConfigure(string? name, JwtBearerOptions options)
        {
            if (name != VocabDefaults.BearerScheme)
                return;
            if (options.UseSecurityTokenValidators)
                throw new InvalidOperationException(
                    "Vocab.Sdk.AspNetCore validates access tokens through JwtBearerOptions.TokenHandlers; UseSecurityTokenValidators is not supported by AddBearer.");
            if (options.TokenHandlers.Count == 0)
                throw new InvalidOperationException("AddBearer needs at least one token handler in JwtBearerOptions.TokenHandlers.");

            var clientId = vocab.Value.ClientId;
            for (var i = 0; i < options.TokenHandlers.Count; i++)
                if (options.TokenHandlers[i] is not MintedForThisAppTokenHandler)
                    options.TokenHandlers[i] = new MintedForThisAppTokenHandler(options.TokenHandlers[i], clientId);
        }
    }

    private sealed class ConfigureJwtBearer(IOptions<VocabOptions> vocab) : IConfigureNamedOptions<JwtBearerOptions>
    {
        public void Configure(string? name, JwtBearerOptions options)
        {
            if (name != VocabDefaults.BearerScheme)
                return;
            var value = vocab.Value;
            options.Authority = value.Authority.ToString().TrimEnd('/');
            options.Audience = value.ApiAudience;
            options.RequireHttpsMetadata = value.Authority.Scheme == Uri.UriSchemeHttps;
        }

        public void Configure(JwtBearerOptions options) => Configure(Options.DefaultName, options);
    }
}
