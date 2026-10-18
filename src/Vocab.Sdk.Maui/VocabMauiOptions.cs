namespace Vocab.Sdk.Maui;

/// <summary>How sign-in behaves in your app. Only <see cref="RedirectUri"/> has no default.</summary>
public sealed class VocabMauiOptions
{
    /// <summary>
    /// Where auth.vo.cab sends the user back: a private-use URI your app owns, such as <c>myapp://callback</c>.
    /// Register exactly this value as a redirect URI in your network's Apps pane, and declare its scheme to the
    /// platform (an intent filter on Android, <c>CFBundleURLTypes</c> on iOS and Mac Catalyst).
    /// </summary>
    public string RedirectUri { get; set; } = string.Empty;

    /// <summary>The scopes to ask for. Default: <see cref="VocabScopes.Default"/> (sign-in plus basic profile).</summary>
    public IList<string> Scopes { get; } = [.. VocabScopes.Default];

    /// <summary>
    /// Refresh the access token when it is this close to expiring, on the next call that needs a token. For a token
    /// that lives less than twice this long, the window is half its lifetime instead (see <see cref="VocabSession.NeedsRefresh"/>).
    /// </summary>
    public TimeSpan RefreshBefore { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Open the sign-in page in a browser session that shares no cookies with the user's browser (iOS and Mac
    /// Catalyst honour this; Android ignores it). Off by default, so a user already signed in to vo.cab in their
    /// browser is not asked for their password again.
    /// </summary>
    public bool PrefersEphemeralWebBrowserSession { get; set; }

    /// <summary>The <c>SecureStorage</c> key the session is kept under. Change it only to keep two sessions apart.</summary>
    public string StorageKey { get; set; } = "vocab.session";

    /// <summary>Throws when the options cannot describe a sign-in. Called by every entry point before use.</summary>
    public void Validate()
    {
        // A bare path parses as an absolute file: URI on some platforms, so the scheme must be spelled out.
        if (!RedirectUri.Contains(':', StringComparison.Ordinal) || !Uri.TryCreate(RedirectUri, UriKind.Absolute, out var redirect) || string.IsNullOrEmpty(redirect.Scheme))
            throw new InvalidOperationException("VocabMauiOptions.RedirectUri must be an absolute URI your app owns, such as myapp://callback.");
        if (Scopes.Count == 0)
            throw new InvalidOperationException("VocabMauiOptions.Scopes must name at least one scope.");
        if (RefreshBefore < TimeSpan.Zero)
            throw new InvalidOperationException("VocabMauiOptions.RefreshBefore cannot be negative.");
        if (string.IsNullOrWhiteSpace(StorageKey))
            throw new InvalidOperationException("VocabMauiOptions.StorageKey is required.");
    }
}
