using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vocab.Sdk;

/// <summary>The caller's identity in your network, as the API sees it: <c>GET /v1/whoami</c>.</summary>
public sealed record WhoAmI(
    [property: JsonPropertyName("profileId")] string ProfileId,
    [property: JsonPropertyName("networkId")] string NetworkId,
    [property: JsonPropertyName("clientId")] string ClientId,
    [property: JsonPropertyName("scopes")] IReadOnlyList<string> Scopes);

/// <summary>Your app's own id for the user's profile in your network.</summary>
public sealed record ExternalIdLink(
    [property: JsonPropertyName("profileId")] string ProfileId,
    [property: JsonPropertyName("externalId")] string ExternalId);

/// <summary>
/// Fields of the user's profile your app may edit (requires <see cref="VocabScopes.ProfileWrite"/>). A field left
/// null is not touched; a field set to the empty string is cleared. The handle is changed through
/// <see cref="VocabClient.SetHandleAsync"/> and email through <see cref="VocabClient.AddEmailAsync"/>.
/// </summary>
public sealed class ProfileUpdate
{
    /// <summary>Up to 100 characters.</summary>
    public string? DisplayName { get; set; }

    /// <summary>Up to 500 characters.</summary>
    public string? Bio { get; set; }

    /// <summary>A media reference returned by <see cref="VocabClient.UploadAvatarAsync"/>; never an arbitrary URL.</summary>
    public string? Avatar { get; set; }

    /// <summary>Up to 50 characters.</summary>
    public string? Pronouns { get; set; }

    /// <summary>An E.164 telephone number, such as <c>+14155550100</c>.</summary>
    public string? Phone { get; set; }

    internal Dictionary<string, string> ToFields()
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        if (DisplayName is not null) fields["displayName"] = DisplayName;
        if (Bio is not null) fields["bio"] = Bio;
        if (Avatar is not null) fields["avatar"] = Avatar;
        if (Pronouns is not null) fields["pronouns"] = Pronouns;
        if (Phone is not null) fields["phone"] = Phone;
        return fields;
    }
}

/// <summary>The outcome of <see cref="VocabClient.AddEmailAsync"/>: the address awaits the user's confirmation.</summary>
public sealed record EmailAddition(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("email")] string Email);

/// <summary>The outcome of <see cref="VocabClient.UploadAvatarAsync"/>.</summary>
public sealed record AvatarUpload(
    [property: JsonPropertyName("avatar")] string Avatar,
    [property: JsonPropertyName("avatarUrl")] string? AvatarUrl);

/// <summary>Tokens from auth.vo.cab (RFC 6749 §5.1 shape).</summary>
public sealed record VocabTokens(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("token_type")] string TokenType,
    [property: JsonPropertyName("expires_in")] int ExpiresIn,
    [property: JsonPropertyName("refresh_token")] string? RefreshToken,
    [property: JsonPropertyName("id_token")] string? IdToken,
    [property: JsonPropertyName("scope")] string? Scope);

/// <summary>
/// A user's profile in your network, as the <c>vo.profile.*</c> scopes release it. The same shape describes the
/// signed-in user (<see cref="VocabAuthClient.GetProfileAsync"/>) and the profiles linked to them
/// (<see cref="VocabClient.ListLinkedProfilesAsync"/>). Fields a scope did not release are null.
/// </summary>
public sealed class VocabProfile
{
    /// <summary>The id of the profile in the network.</summary>
    [JsonPropertyName(VocabClaims.ProfileId)] public string ProfileId { get; init; } = string.Empty;
    /// <summary>The network the profile belongs to.</summary>
    [JsonPropertyName(VocabClaims.NetworkId)] public string NetworkId { get; init; } = string.Empty;
    /// <summary>The profile's handle (basic scope).</summary>
    [JsonPropertyName(VocabClaims.Handle)] public string? Handle { get; init; }
    /// <summary>The profile's display name (basic scope).</summary>
    [JsonPropertyName("name")] public string? DisplayName { get; init; }
    /// <summary>The profile's avatar as an absolute URL (basic scope).</summary>
    [JsonPropertyName(VocabClaims.AvatarUrl)] public string? AvatarUrl { get; init; }
    /// <summary>The names of the roles the profile holds in the network (basic scope).</summary>
    [JsonPropertyName(VocabClaims.Roles)] public IReadOnlyList<string>? Roles { get; init; }
    /// <summary>The account's email address (contact scope).</summary>
    [JsonPropertyName("email")] public string? Email { get; init; }
    /// <summary>Whether the email address is verified (contact scope).</summary>
    [JsonPropertyName("email_verified")] public bool? EmailVerified { get; init; }
    /// <summary>The profile's phone number (contact scope).</summary>
    [JsonPropertyName(VocabClaims.Phone)] public string? Phone { get; init; }

    /// <summary>Claims this SDK version does not model, kept so nothing the API says is lost.</summary>
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; init; }
}

internal sealed record LinkedProfilesResponse(
    [property: JsonPropertyName("profiles")] IReadOnlyList<VocabProfile> Profiles);

internal sealed record ApiError(
    [property: JsonPropertyName("error")] string? Error,
    [property: JsonPropertyName("code")] string? Code);
