using System.Security.Claims;

namespace Vocab.Sdk.AspNetCore;

/// <summary>What vo.cab says about the signed-in user, read off the claims the SDK put on the principal.</summary>
public sealed record VocabIdentity
{
    /// <summary>The user's stable vo.cab account id, the same in every network.</summary>
    public required string UserId { get; init; }

    /// <summary>The user's vo.cab username, when the token carried it.</summary>
    public string? Username { get; init; }

    /// <summary>The id of the user's profile in your network, or null when they have none.</summary>
    public string? ProfileId { get; init; }

    /// <summary>Your network's id, when the token named it.</summary>
    public string? NetworkId { get; init; }

    /// <summary>The profile's handle in your network.</summary>
    public string? Handle { get; init; }

    /// <summary>The profile's display name in your network.</summary>
    public string? DisplayName { get; init; }

    /// <summary>The profile's avatar URL in your network.</summary>
    public string? AvatarUrl { get; init; }

    /// <summary>The names of the roles the profile holds in your network.</summary>
    public IReadOnlyList<string> Roles { get; init; } = [];

    /// <summary>The scopes the access token carries.</summary>
    public IReadOnlyList<string> Scopes { get; init; } = [];

    /// <summary>The compiled <c>net:*</c> permissions the access token carries.</summary>
    public IReadOnlyList<string> Permissions { get; init; } = [];

    /// <summary>True when the user has a profile in your network.</summary>
    public bool HasProfile => !string.IsNullOrEmpty(ProfileId);
}

/// <summary>Reads vo.cab claims off a <see cref="ClaimsPrincipal"/>.</summary>
public static class VocabPrincipalExtensions
{
    /// <summary>The vo.cab identity on this principal, or null when it is not signed in through vo.cab.</summary>
    public static VocabIdentity? GetVocab(this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        var userId = principal.FindFirst(VocabClaims.Subject)?.Value
                     ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return null;

        return new VocabIdentity
        {
            UserId = userId,
            Username = principal.FindFirst(VocabClaims.PreferredUsername)?.Value,
            ProfileId = principal.FindFirst(VocabClaims.ProfileId)?.Value,
            NetworkId = principal.FindFirst(VocabClaims.NetworkId)?.Value,
            Handle = principal.FindFirst(VocabClaims.Handle)?.Value,
            DisplayName = principal.FindFirst("name")?.Value ?? principal.FindFirst(ClaimTypes.Name)?.Value,
            AvatarUrl = principal.FindFirst(VocabClaims.AvatarUrl)?.Value,
            Roles = principal.FindAll(VocabClaims.Roles).Select(c => c.Value).Where(v => v.Length > 0).ToArray(),
            Scopes = SpaceSeparated(principal, VocabClaims.Scope),
            Permissions = SpaceSeparated(principal, VocabClaims.Permissions),
        };
    }

    /// <summary>True when the principal's access token carries <paramref name="scope"/>.</summary>
    public static bool HasVocabScope(this ClaimsPrincipal principal, string scope) =>
        SpaceSeparated(principal, VocabClaims.Scope).Contains(scope, StringComparer.Ordinal);

    /// <summary>True when the principal's access token carries the compiled permission <paramref name="permission"/> (a <c>net:*</c> value).</summary>
    public static bool HasVocabPermission(this ClaimsPrincipal principal, string permission) =>
        SpaceSeparated(principal, VocabClaims.Permissions).Contains(permission, StringComparer.Ordinal);

    private static string[] SpaceSeparated(ClaimsPrincipal principal, string claimType) =>
        principal.FindAll(claimType)
            .SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
}
