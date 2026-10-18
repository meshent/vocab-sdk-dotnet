using Microsoft.Maui.Authentication;

namespace Vocab.Sdk.Maui;

/// <summary>
/// Shows the user auth.vo.cab's sign-in page and brings them back. The default opens the system browser through
/// <see cref="IWebAuthenticator"/>; tests substitute one that answers at once.
/// </summary>
public interface IVocabAuthenticator
{
    /// <summary>
    /// Sends the user to <paramref name="authorizeUrl"/> and returns the query parameters of the
    /// <paramref name="callbackUrl"/> they came back on (<c>code</c> and <c>state</c>, or <c>error</c>).
    /// </summary>
    /// <exception cref="OperationCanceledException">The user closed the browser without finishing.</exception>
    Task<IReadOnlyDictionary<string, string>> AuthenticateAsync(Uri authorizeUrl, Uri callbackUrl, CancellationToken cancellationToken = default);
}

/// <summary>The default <see cref="IVocabAuthenticator"/>: .NET MAUI's <see cref="IWebAuthenticator"/> (the system browser).</summary>
public sealed class WebAuthenticatorVocabAuthenticator(IWebAuthenticator web, VocabMauiOptions options) : IVocabAuthenticator
{
    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, string>> AuthenticateAsync(Uri authorizeUrl, Uri callbackUrl, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(authorizeUrl);
        ArgumentNullException.ThrowIfNull(callbackUrl);

        var request = new WebAuthenticatorOptions
        {
            Url = authorizeUrl,
            CallbackUrl = callbackUrl,
            PrefersEphemeralWebBrowserSession = options.PrefersEphemeralWebBrowserSession,
        };
#if NET10_0_OR_GREATER
        var result = await web.AuthenticateAsync(request, cancellationToken);
#else
        // MAUI 8's browser call takes no cancellation token, so cancellation stops the wait; the browser tab, if
        // still open, comes back to an app that is no longer listening.
        var result = await web.AuthenticateAsync(request).WaitAsync(cancellationToken);
#endif

        return new Dictionary<string, string>(result.Properties, StringComparer.Ordinal);
    }
}
