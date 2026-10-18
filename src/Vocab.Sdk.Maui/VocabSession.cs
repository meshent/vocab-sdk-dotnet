using System.Text.Json.Serialization;

namespace Vocab.Sdk.Maui;

/// <summary>The signed-in user's tokens, as <see cref="VocabSignIn"/> keeps them in the device's secure storage.</summary>
public sealed record VocabSession
{
    /// <summary>The access token for auth.vo.cab's profile endpoint and the network API.</summary>
    [JsonPropertyName("access_token")]
    public required string AccessToken { get; init; }

    /// <summary>The refresh token, or null when auth.vo.cab issued none (the session then ends at <see cref="ExpiresAt"/>).</summary>
    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; init; }

    /// <summary>When the access token expires.</summary>
    [JsonPropertyName("expires_at")]
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>The id token from sign-in, kept as issued. The SDK does not validate it; identity comes from the access token's bearer calls.</summary>
    [JsonPropertyName("id_token")]
    public string? IdToken { get; init; }

    /// <summary>The scopes auth.vo.cab granted, space separated, when it said.</summary>
    [JsonPropertyName("scope")]
    public string? Scope { get; init; }

    /// <summary>True when the access token expires within <paramref name="window"/> of <paramref name="now"/> (or has already).</summary>
    public bool ExpiresWithin(TimeSpan window, DateTimeOffset now) => ExpiresAt - now <= window;

    /// <summary>True when the access token has expired at <paramref name="now"/>.</summary>
    public bool IsExpired(DateTimeOffset now) => ExpiresAt <= now;

    /// <summary>A session from the tokens auth.vo.cab just issued, stamped against <paramref name="now"/>.</summary>
    public static VocabSession From(VocabTokens tokens, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        return new VocabSession
        {
            AccessToken = tokens.AccessToken,
            RefreshToken = tokens.RefreshToken,
            ExpiresAt = now.AddSeconds(tokens.ExpiresIn),
            IdToken = tokens.IdToken,
            Scope = tokens.Scope,
        };
    }
}

/// <summary>Raised by <see cref="VocabSignIn.SessionChanged"/> when the user signs in, the tokens refresh, or the session ends.</summary>
public sealed class VocabSessionChangedEventArgs(VocabSession? session) : EventArgs
{
    /// <summary>The session now in force, or null when the user is signed out.</summary>
    public VocabSession? Session { get; } = session;
}
