namespace Vocab.Sdk;

/// <summary>The scopes a network app can ask for at sign-in. Ask for the least your app needs.</summary>
public static class VocabScopes
{
    /// <summary>OpenID Connect sign-in: an id token naming the user.</summary>
    public const string OpenId = "openid";

    /// <summary>The user's public profile in your network: handle, display name, avatar, roles. Needed for every network API call.</summary>
    public const string ProfileBasic = "vo.profile.basic";

    /// <summary>The user's contact details in your network (email, phone), consented separately.</summary>
    public const string ProfileContact = "vo.profile.contact";

    /// <summary>Edit the user's profile in your network (display name, bio, avatar, pronouns, phone, handle).</summary>
    public const string ProfileWrite = "vo.profile.write";

    /// <summary>List the other profiles in your network that have shared their basic profile with this user.</summary>
    public const string ProfileLinked = "vo.profile.linked";

    /// <summary>What a typical app asks for: sign-in plus the user's basic profile.</summary>
    public static IReadOnlyList<string> Default { get; } = [OpenId, ProfileBasic];

    /// <summary>Joins scopes the way the wire expects them: space separated.</summary>
    public static string Join(IEnumerable<string> scopes) => string.Join(' ', scopes);
}
