namespace Vocab.Sdk.AspNetCore;

/// <summary>How the sign-in flow behaves. The defaults suit a server-rendered app that reads the user's basic profile.</summary>
public sealed class VocabSignInOptions
{
    /// <summary>The scopes to ask for. Default: <see cref="VocabScopes.Default"/> (sign-in plus basic profile).</summary>
    public IList<string> Scopes { get; } = [.. VocabScopes.Default];

    /// <summary>Where auth.vo.cab sends the user back. Register <c>https://your-app{CallbackPath}</c> in the Apps pane.</summary>
    public string CallbackPath { get; set; } = "/signin-vocab";

    /// <summary>
    /// After sign-in, call auth.vo.cab's profile endpoint and put the user's handle, display name, avatar and roles
    /// in your network on the principal as claims. Costs one request per sign-in.
    /// </summary>
    public bool LoadProfileClaims { get; set; } = true;

    /// <summary>Refresh the access token when it is this close to expiring, on the next request that uses the session.</summary>
    public TimeSpan RefreshBefore { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>How long a signed-in session lasts without activity.</summary>
    public TimeSpan SessionLifetime { get; set; } = TimeSpan.FromDays(14);
}
