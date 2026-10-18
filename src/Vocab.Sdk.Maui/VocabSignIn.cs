using System.Security.Cryptography;
using System.Text;

namespace Vocab.Sdk.Maui;

/// <summary>
/// Sign in with vo.cab for a .NET MAUI app. One instance per app (registered by <c>AddVocab</c>): it runs the
/// authorization code flow with PKCE through the system browser, keeps the resulting session in secure storage,
/// hands <see cref="VocabClient"/> a fresh access token on every call (refreshing behind the scenes), and signs out
/// by revoking the refresh token.
/// </summary>
/// <remarks>
/// <para>
/// auth.vo.cab rotates refresh tokens and treats a second use of a rotated-away token as theft: it revokes the
/// whole session. Every refresh therefore runs under one gate, so however many callers ask for a token at once,
/// the refresh token is spent exactly once and every caller gets the new tokens; a caller that joins the queue
/// after the refresh finished finds the fresh session and refreshes nothing. The refresh request itself is not tied to
/// the first caller's cancellation, because its result serves them all and a request the server has already
/// processed cannot be un-sent.
/// </para>
/// <para>
/// Your app is a public client: it holds no client secret, and auth.vo.cab refuses one. Leave
/// <see cref="VocabOptions.ClientSecret"/> null.
/// </para>
/// </remarks>
public sealed class VocabSignIn(VocabAuthClient auth, IVocabSessionStore store, IVocabAuthenticator authenticator, VocabMauiOptions options, TimeProvider time)
    : IVocabTokenProvider, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private VocabSession? _session;
    private bool _loaded;

    /// <summary>Raised after sign-in, after a refresh, and after sign-out (or a refresh that found the session revoked).</summary>
    public event EventHandler<VocabSessionChangedEventArgs>? SessionChanged;

    /// <summary>The current session as stored, without refreshing it, or null when the user is signed out.</summary>
    public async Task<VocabSession?> GetSessionAsync(CancellationToken cancellationToken = default)
    {
        if (_loaded)
            return _session;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await LoadLockedAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>True when a session is stored. It may still need a refresh, which the next token request performs.</summary>
    public async Task<bool> IsSignedInAsync(CancellationToken cancellationToken = default) =>
        await GetSessionAsync(cancellationToken) is not null;

    /// <summary>
    /// Opens auth.vo.cab in the system browser, waits for the user to come back on <see cref="VocabMauiOptions.RedirectUri"/>,
    /// redeems the code and stores the session. A user with no profile in your network is offered one by auth.vo.cab
    /// before they return.
    /// </summary>
    /// <param name="scopes">The scopes to ask for; <see cref="VocabMauiOptions.Scopes"/> when null.</param>
    /// <param name="cancellationToken">Cancels waiting for the browser; a code already redeemed is still stored.</param>
    /// <exception cref="VocabSignInException">auth.vo.cab sent the user back with an error, or the callback failed the state check.</exception>
    /// <exception cref="VocabApiException">auth.vo.cab refused the code.</exception>
    /// <exception cref="OperationCanceledException">The user closed the browser without finishing.</exception>
    public async Task<VocabSession> SignInAsync(IEnumerable<string>? scopes = null, CancellationToken cancellationToken = default)
    {
        options.Validate();
        var redirect = new Uri(options.RedirectUri, UriKind.Absolute);
        var (verifier, challenge) = Pkce.Create();
        var state = RandomToken();

        var authorizeUrl = auth.BuildAuthorizeUrl(options.RedirectUri, state, challenge, scopes ?? options.Scopes);
        var callback = await authenticator.AuthenticateAsync(authorizeUrl, redirect, cancellationToken);

        // The state check comes first: an error carrying someone else's state is not this sign-in's error either.
        if (!callback.TryGetValue("state", out var returnedState) || !FixedTimeEquals(returnedState, state))
            throw new VocabSignInException(VocabSignInException.InvalidState, "The sign-in callback did not match the sign-in this app started.");
        if (callback.TryGetValue("error", out var error) && !string.IsNullOrEmpty(error))
            throw new VocabSignInException(error, callback.TryGetValue("error_description", out var description) && !string.IsNullOrEmpty(description)
                ? description
                : $"auth.vo.cab answered '{error}'.");
        if (!callback.TryGetValue("code", out var code) || string.IsNullOrEmpty(code))
            throw new VocabSignInException(VocabSignInException.MissingCode, "The sign-in callback carried no authorization code.");

        var tokens = await auth.RedeemCodeAsync(code, options.RedirectUri, verifier, cancellationToken);
        var session = VocabSession.From(tokens, time.GetUtcNow());

        await _gate.WaitAsync(CancellationToken.None);
        try
        {
            await SetSessionLockedAsync(session, CancellationToken.None);
        }
        finally
        {
            _gate.Release();
        }
        return session;
    }

    /// <summary>
    /// The access token to send, refreshed first when it is within <see cref="VocabMauiOptions.RefreshBefore"/> of
    /// expiring. Null when the user is signed out, or when the session turned out to be revoked (it is then cleared).
    /// </summary>
    /// <remarks>
    /// A refresh that fails for a reason other than the session being revoked (the network is down, auth.vo.cab is
    /// unavailable) is not fatal while the current token is still valid: that token is returned and the refresh is
    /// retried on the next call. Once the token has expired, the failure is thrown.
    /// </remarks>
    public async ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        var session = await GetSessionAsync(cancellationToken);
        if (session is null)
            return null;
        if (!session.ExpiresWithin(options.RefreshBefore, time.GetUtcNow()))
            return session.AccessToken;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var current = await LoadLockedAsync(cancellationToken);
            if (current is null)
                return null;

            var now = time.GetUtcNow();
            // Another caller refreshed while this one waited at the gate: use what it brought back.
            if (!current.ExpiresWithin(options.RefreshBefore, now))
                return current.AccessToken;

            if (current.RefreshToken is null)
            {
                if (!current.IsExpired(now))
                    return current.AccessToken;
                await SetSessionLockedAsync(null, cancellationToken);
                return null;
            }

            VocabTokens tokens;
            try
            {
                tokens = await auth.RefreshAsync(current.RefreshToken, CancellationToken.None);
            }
            catch (VocabApiException ex) when (ex.Code is VocabErrorCodes.InvalidGrant or VocabErrorCodes.InvalidToken)
            {
                // The session is gone on the server (revoked, expired, or its refresh token was reused): sign out locally.
                await SetSessionLockedAsync(null, cancellationToken);
                return null;
            }
            catch (Exception ex) when (ex is HttpRequestException or VocabApiException or TaskCanceledException && !current.IsExpired(now))
            {
                return current.AccessToken;
            }

            var fresh = VocabSession.From(tokens, now) with
            {
                // auth.vo.cab rotates the refresh token on every refresh; should it ever answer without one, the current token stays valid.
                RefreshToken = tokens.RefreshToken ?? current.RefreshToken,
                IdToken = tokens.IdToken ?? current.IdToken,
                Scope = tokens.Scope ?? current.Scope,
            };
            await SetSessionLockedAsync(fresh, cancellationToken);
            return fresh.AccessToken;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Signs the user out of your app: forgets the session, then revokes its refresh token at auth.vo.cab so it
    /// cannot be used again. The user stays signed in to vo.cab itself, as with any "sign in with" provider.
    /// </summary>
    /// <returns>
    /// True when the refresh token was revoked (or there was none to revoke); false when auth.vo.cab could not be
    /// reached or refused, in which case the app is signed out all the same and the token dies at its own expiry.
    /// </returns>
    public async Task<bool> SignOutAsync(CancellationToken cancellationToken = default)
    {
        VocabSession? session;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            session = await LoadLockedAsync(cancellationToken);
            if (session is null)
                return true;
            await SetSessionLockedAsync(null, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }

        if (session.RefreshToken is null)
            return true;
        try
        {
            await auth.RevokeAsync(session.RefreshToken, cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or VocabApiException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public void Dispose() => _gate.Dispose();

    private async Task<VocabSession?> LoadLockedAsync(CancellationToken cancellationToken)
    {
        if (!_loaded)
        {
            _session = await store.LoadAsync(cancellationToken);
            _loaded = true;
        }
        return _session;
    }

    private async Task SetSessionLockedAsync(VocabSession? session, CancellationToken cancellationToken)
    {
        if (session is null)
            await store.ClearAsync(cancellationToken);
        else
            await store.SaveAsync(session, cancellationToken);
        _session = session;
        _loaded = true;
        SessionChanged?.Invoke(this, new VocabSessionChangedEventArgs(session));
    }

    private static string RandomToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool FixedTimeEquals(string left, string right) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));
}
