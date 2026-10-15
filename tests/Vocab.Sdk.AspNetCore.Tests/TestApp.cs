using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Vocab.Sdk.AspNetCore.Tests;

/// <summary>An in-memory app wired with the SDK against a <see cref="FakeAuthority"/> at https://auth.test.</summary>
internal static class TestApp
{
    public static readonly Uri Authority = new("https://auth.test");

    public static async Task<WebApplication> StartAsync(
        FakeAuthority authority,
        Func<IVocabBuilder, IVocabBuilder> register,
        Action<WebApplication> map,
        string? clientSecret = "s3cret")
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        register(builder.Services.AddVocab(o =>
        {
            o.NetworkId = "example";
            o.ClientId = "app";
            o.ClientSecret = clientSecret;
            o.Authority = Authority;
        }));
        builder.Services.AddHttpClient(VocabDefaults.AuthHttpClient).ConfigurePrimaryHttpMessageHandler(() => authority);
        builder.Services.AddAuthorization();

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        map(app);
        await app.StartAsync();
        return app;
    }

    /// <summary>Endpoints a sign-in test needs: a cookie sign-in with the given tokens, sign-out, and a probe.</summary>
    public static void MapSession(WebApplication app)
    {
        app.MapPost("/test/signin", async (HttpContext http, int expiresIn) =>
        {
            var identity = new ClaimsIdentity([new Claim(VocabClaims.Subject, "u1")], VocabDefaults.CookieScheme);
            var properties = new AuthenticationProperties();
            properties.StoreTokens(
            [
                new AuthenticationToken { Name = "access_token", Value = "a1" },
                new AuthenticationToken { Name = "refresh_token", Value = "r1" },
                new AuthenticationToken { Name = "expires_at", Value = DateTimeOffset.UtcNow.AddSeconds(expiresIn).ToString("o", CultureInfo.InvariantCulture) },
            ]);
            await http.SignInAsync(VocabDefaults.CookieScheme, new ClaimsPrincipal(identity), properties);
            return Results.Ok();
        });

        // No scheme named: the SDK's default sign-out scheme must be the cookie.
        app.MapPost("/signout", async (HttpContext http) =>
        {
            await http.SignOutAsync();
            return Results.Ok();
        });

        // Who the cookie says we are, and which access token the session holds.
        app.MapGet("/me", async (HttpContext http) =>
            http.User.Identity?.IsAuthenticated == true
                ? $"authenticated:{await http.GetTokenAsync(VocabDefaults.CookieScheme, "access_token")}"
                : "anonymous");
    }

    public static async Task<string> SignInAsync(HttpClient client, int expiresIn)
    {
        using var response = await client.PostAsync(new Uri($"/test/signin?expiresIn={expiresIn}", UriKind.Relative), content: null);
        response.EnsureSuccessStatusCode();
        return SessionCookie(response) ?? throw new InvalidOperationException("Sign-in set no session cookie.");
    }

    public static string? SessionCookie(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var cookies))
            return null;
        var session = cookies.FirstOrDefault(c => c.StartsWith(".Vocab.Session=", StringComparison.Ordinal));
        return session?[..session.IndexOf(';', StringComparison.Ordinal)];
    }

    public static HttpRequestMessage Request(HttpMethod method, string path, string cookie)
    {
        var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        request.Headers.Add("Cookie", cookie);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));
        return request;
    }
}
