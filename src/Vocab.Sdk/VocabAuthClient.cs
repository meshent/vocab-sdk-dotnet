using System.Net.Http.Headers;
using System.Net.Http.Json;
using Vocab.Sdk.Internal;

namespace Vocab.Sdk;

/// <summary>
/// The parts of auth.vo.cab an app calls directly: redeeming the authorization code it received, refreshing and
/// revoking tokens, and reading the signed-in user's profile claims. The sign-in redirect itself is the platform
/// package's job (or your OpenID Connect library's).
/// </summary>
/// <remarks>
/// auth.vo.cab's token endpoints take JSON bodies. This client sends the right shape so you never have to know.
/// Construct it with an <see cref="HttpClient"/> whose <see cref="HttpClient.BaseAddress"/> is
/// <see cref="VocabOptions.Authority"/>.
/// </remarks>
public sealed class VocabAuthClient(HttpClient http, VocabOptions options)
{
    /// <summary>The authorization endpoint apps redirect the user to.</summary>
    public Uri AuthorizeEndpoint => new(options.Authority, "/oauth/authorize");

    /// <summary>
    /// Builds the sign-in URL for the authorization code flow with PKCE (S256). Generate <paramref name="codeChallenge"/>
    /// with <see cref="Pkce"/> and keep its verifier for <see cref="RedeemCodeAsync"/>.
    /// </summary>
    public Uri BuildAuthorizeUrl(string redirectUri, string state, string codeChallenge, IEnumerable<string>? scopes = null, string? nonce = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(redirectUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(codeChallenge);
        options.Validate();

        var query = new Dictionary<string, string?>
        {
            ["response_type"] = "code",
            ["client_id"] = options.ClientId,
            ["redirect_uri"] = redirectUri,
            ["scope"] = VocabScopes.Join(scopes ?? VocabScopes.Default),
            ["state"] = state,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
        };
        if (!string.IsNullOrEmpty(nonce))
            query["nonce"] = nonce;

        var builder = new UriBuilder(AuthorizeEndpoint)
        {
            Query = string.Join('&', query.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value!)}")),
        };
        return builder.Uri;
    }

    /// <summary>Exchanges the authorization code for tokens (RFC 6749 §4.1.3 with PKCE).</summary>
    public async Task<VocabTokens> RedeemCodeAsync(string code, string redirectUri, string codeVerifier, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(redirectUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(codeVerifier);
        options.Validate();

        var body = new Dictionary<string, string?>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = options.ClientId,
            ["code_verifier"] = codeVerifier,
            ["client_secret"] = options.ClientSecret,
        };
        using var response = await PostJsonAsync("/oauth/token", body, cancellationToken);
        return await VocabJson.ReadAsync<VocabTokens>(response, cancellationToken);
    }

    /// <summary>Trades a refresh token for a new access token (and, usually, a rotated refresh token).</summary>
    public async Task<VocabTokens> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);
        options.Validate();

        var body = new Dictionary<string, string?>
        {
            ["refresh_token"] = refreshToken,
            ["client_secret"] = options.ClientSecret,
        };
        using var response = await PostJsonAsync("/auth/refresh", body, cancellationToken);
        return await VocabJson.ReadAsync<VocabTokens>(response, cancellationToken);
    }

    /// <summary>Revokes a refresh token (sign-out on the server side). Succeeds even when the token is already gone.</summary>
    public async Task RevokeAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);
        var form = new Dictionary<string, string> { ["token"] = refreshToken };
        if (!string.IsNullOrEmpty(options.ClientSecret))
            form["client_secret"] = options.ClientSecret;

        using var content = new FormUrlEncodedContent(form);
        using var response = await http.PostAsync(new Uri("/oauth/revoke", UriKind.Relative), content, cancellationToken);
        await VocabJson.EnsureSuccessAsync(response, cancellationToken);
    }

    /// <summary>
    /// The signed-in user's profile in your network, as the token's <c>vo.profile.*</c> scopes release it. Null when
    /// the user has no profile in your network (they declined to create one at sign-in).
    /// </summary>
    public async Task<VocabProfile?> GetProfileAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/oauth/profile", UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        await VocabJson.EnsureSuccessAsync(response, cancellationToken);

        var profile = await response.Content.ReadFromJsonAsync<VocabProfile>(VocabJson.Options, cancellationToken);
        // A token with no profile scope gets an empty object: no profile id, so no profile to speak of.
        return profile is { ProfileId.Length: > 0 } ? profile : null;
    }

    private Task<HttpResponseMessage> PostJsonAsync(string path, Dictionary<string, string?> body, CancellationToken cancellationToken)
    {
        // A public app sends no client_secret at all: auth.vo.cab refuses a stray one, and a null is still a key.
        var fields = body.Where(p => p.Value is not null).ToDictionary(p => p.Key, p => p.Value!, StringComparer.Ordinal);
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative))
        {
            Content = JsonContent.Create(fields, options: VocabJson.Options),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return http.SendAsync(request, cancellationToken);
    }
}
