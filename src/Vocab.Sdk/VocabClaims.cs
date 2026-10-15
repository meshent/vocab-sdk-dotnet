namespace Vocab.Sdk;

/// <summary>
/// The claim names vo.cab tokens and the profile endpoint use. Standard OpenID Connect claims keep their names;
/// the profile-in-your-network claims are prefixed <c>vo_</c>.
/// </summary>
public static class VocabClaims
{
    /// <summary>The user's stable account id (the token subject). The same across every network.</summary>
    public const string Subject = "sub";

    /// <summary>The user's vo.cab username.</summary>
    public const string PreferredUsername = "preferred_username";

    /// <summary>The id of the user's profile in your network.</summary>
    public const string ProfileId = "vo_profile_id";

    /// <summary>Your network's id.</summary>
    public const string NetworkId = "vo_network_id";

    /// <summary>The profile's handle in your network.</summary>
    public const string Handle = "vo_handle";

    /// <summary>The profile's avatar, as an absolute URL.</summary>
    public const string AvatarUrl = "vo_avatar_url";

    /// <summary>The profile's phone number (requires <see cref="VocabScopes.ProfileContact"/>).</summary>
    public const string Phone = "vo_phone";

    /// <summary>The names of the roles the profile holds in your network.</summary>
    public const string Roles = "vo_roles";

    /// <summary>Access-token claim: the compiled <c>net:*</c> permissions of this grant, space separated.</summary>
    public const string Permissions = "vo_perm";

    /// <summary>Access-token claim: the scopes this token carries, space separated.</summary>
    public const string Scope = "scope";

    /// <summary>Access-token claim: the client id the token was minted for.</summary>
    public const string ClientId = "client_id";
}
