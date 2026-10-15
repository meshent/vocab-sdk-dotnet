namespace Vocab.Sdk.AspNetCore;

/// <summary>Scheme names the SDK registers. Use them when you compose your own authentication policies.</summary>
public static class VocabDefaults
{
    /// <summary>The OpenID Connect scheme that redirects to auth.vo.cab (the challenge scheme after <c>AddSignIn</c>).</summary>
    public const string SignInScheme = "Vocab";

    /// <summary>The cookie scheme that holds the signed-in session (the default scheme after <c>AddSignIn</c>).</summary>
    public const string CookieScheme = "Vocab.Cookie";

    /// <summary>The JWT bearer scheme that accepts vo.cab access tokens on your API (after <c>AddBearer</c>).</summary>
    public const string BearerScheme = "Vocab.Bearer";

    /// <summary>The named <see cref="HttpClient"/> that talks to auth.vo.cab.</summary>
    public const string AuthHttpClient = "Vocab.Auth";

    /// <summary>The named <see cref="HttpClient"/> that talks to your network's API.</summary>
    public const string ApiHttpClient = "Vocab.Api";
}
