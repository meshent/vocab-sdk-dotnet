using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

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
/// this app sends each refresh token once and every caller gets the new tokens; a caller that joins the queue
/// after the refresh finished finds the fresh session and refreshes nothing. The new tokens take effect in memory
/// the moment auth.vo.cab answers, before they are persisted, so a store that fails cannot cause a replay either.
/// The refresh request itself is not tied to the first caller's cancellation, because its result serves them all
/// and a request the server has already processed cannot be un-sent. What rotation itself cannot cover is a
/// response lost in transit after the server rotated: the retry then carries a token the server no longer accepts
/// and the user signs in again.
/// </para>
/// <para>
/// Revocation is per session: auth.vo.cab's <c>/oauth/revoke</c> ends the one session the given refresh token
/// belongs to, not every grant the user holds for your app. Signing out on one device, or signing in again on
/// this one (which revokes the session it replaces), leaves the user's other devices signed in.
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
    // Written only under the gate, in this order (session, then loaded); read without it on the fast paths.
    private volatile VocabSession? _session;
    private volatile bool _loaded;
    // Counts the session changes, so a change that finished its work late (a slow revoke) does not announce itself
    // after a later change already has.
    private int _version;

    /// <summary>
    /// Raised once per change of the session in force: after sign-in, after a refresh, and after sign-out (or a
    /// refresh that found the session revoked), when that change's own work is complete. Changes announce
    /// themselves in order: a change overtaken by a later one is not announced, so the last event always describes
    /// the current session. Handlers run on the calling thread.
    /// </summary>
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
    /// before they return. A session that was already in force is replaced and its refresh token revoked.
    /// </summary>
    /// <param name="scopes">The scopes to ask for; <see cref="VocabMauiOptions.Scopes"/> when null.</param>
    /// <param name="cancellationToken">Cancels waiting for the browser; a code already redeemed is still kept.</param>
    /// <exception cref="VocabSignInException">auth.vo.cab sent the user back with an error, or the callback failed the state check.</exception>
    /// <exception cref="VocabApiException">auth.vo.cab refused the code.</exception>
    /// <exception cref="OperationCanceledException">The user closed the browser without finishing.</exception>
    /// <remarks>
    /// A failure to persist the new session is thrown after the session has taken effect in memory and been
    /// announced: the user is signed in for this launch, and the app should treat the exception as a storage
    /// problem, not a sign-in failure.
    /// </remarks>
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

        VocabSession? previous;
        int version;
        ExceptionDispatchInfo? storeFailure;
        await _gate.WaitAsync(CancellationToken.None);
        try
        {
            previous = await LoadLockedAsync(CancellationToken.None);
            version = SwapLocked(session);
            storeFailure = await TryPersistLockedAsync(session);
        }
        finally
        {
            _gate.Release();
        }

        try
        {
            // The old session's refresh token is no longer reachable from this app, so it may as well die now.
            if (previous?.RefreshToken is { } old && old != session.RefreshToken)
                await TryRevokeAsync(old, CancellationToken.None);
        }
        finally
        {
            RaiseSessionChanged(session, version);
        }

        storeFailure?.Throw();
        return session;
    }

    /// <summary>
    /// The access token to send, refreshed first when it is within <see cref="VocabMauiOptions.RefreshBefore"/> of
    /// expiring (or within half its lifetime, for a token shorter than twice that window). Null when the user is
    /// signed out, or when the session turned out to be revoked (it is then cleared).
    /// </summary>
    /// <remarks>
    /// A refresh that fails for a reason other than the session being revoked (the network is down, auth.vo.cab is
    /// unavailable or answered nonsense) is not fatal while the current token is still valid: that token is returned
    /// and the refresh is retried on the next call. Once the token has expired, the failure is thrown. New tokens
    /// that the store then fails to persist stay in force in memory for this launch, and the token is returned.
    /// </remarks>
    public async ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        var session = await GetSessionAsync(cancellationToken);
        if (session is null)
            return null;
        if (!session.NeedsRefresh(options.RefreshBefore, time.GetUtcNow()))
            return session.AccessToken;

        VocabSession? result;
        int version;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var current = await LoadLockedAsync(cancellationToken);
            if (current is null)
                return null;

            var now = time.GetUtcNow();
            // Another caller refreshed while this one waited at the gate: use what it brought back.
            if (!current.NeedsRefresh(options.RefreshBefore, now))
                return current.AccessToken;

            if (current.RefreshToken is null)
            {
                if (!current.IsExpired(now))
                    return current.AccessToken;
                result = null;
            }
            else
            {
                VocabTokens? tokens;
                try
                {
                    tokens = await auth.RefreshAsync(current.RefreshToken, CancellationToken.None);
                }
                catch (VocabApiException ex) when (ex.Code is VocabErrorCodes.InvalidGrant or VocabErrorCodes.InvalidToken)
                {
                    // The session is gone on the server (revoked, expired, or its refresh token was reused): sign out locally.
                    tokens = null;
                }
                catch (Exception ex) when (ex is HttpRequestException or VocabApiException or TaskCanceledException or JsonException && !current.IsExpired(now))
                {
                    return current.AccessToken;
                }

                result = tokens is null
                    ? null
                    : VocabSession.From(tokens, now) with
                    {
                        // auth.vo.cab rotates the refresh token on every refresh; should it ever answer without one, the current token stays valid.
                        RefreshToken = tokens.RefreshToken ?? current.RefreshToken,
                        IdToken = tokens.IdToken ?? current.IdToken,
                        Scope = tokens.Scope ?? current.Scope,
                    };
            }

            version = SwapLocked(result);
            // A store that fails here changes nothing: the session in force is the one in memory, nothing can replay
            // the old refresh token, and the store is tried again at the next change.
            await TryPersistLockedAsync(result);
        }
        finally
        {
            _gate.Release();
        }

        RaiseSessionChanged(result, version);
        return result?.AccessToken;
    }

    /// <summary>
    /// Signs the user out of your app: forgets the session, then revokes its refresh token at auth.vo.cab so it
    /// cannot be used again. The user stays signed in to vo.cab itself, as with any "sign in with" provider.
    /// </summary>
    /// <returns>
    /// True when the refresh token was revoked (or there was none to revoke); false when auth.vo.cab could not be
    /// reached, timed out or refused, in which case the app is signed out all the same and the token dies at its
    /// own expiry.
    /// </returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled. The app is signed out once the session was forgotten.</exception>
    /// <remarks>
    /// A store that fails to forget the session does not stop the revocation: the refresh token is revoked, the
    /// change announced, and the store's failure then thrown. The next launch finds the stale session, fails to
    /// refresh it and clears it.
    /// </remarks>
    public async Task<bool> SignOutAsync(CancellationToken cancellationToken = default)
    {
        VocabSession? session;
        int version;
        ExceptionDispatchInfo? storeFailure;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            session = await LoadLockedAsync(cancellationToken);
            if (session is null)
                return true;
            version = SwapLocked(null);
            storeFailure = await TryPersistLockedAsync(null);
        }
        finally
        {
            _gate.Release();
        }

        bool revoked;
        try
        {
            revoked = session.RefreshToken is null || await TryRevokeAsync(session.RefreshToken, cancellationToken);
        }
        finally
        {
            RaiseSessionChanged(null, version);
        }

        storeFailure?.Throw();
        return revoked;
    }

    /// <inheritdoc />
    public void Dispose() => _gate.Dispose();

    private async Task<bool> TryRevokeAsync(string refreshToken, CancellationToken cancellationToken)
    {
        try
        {
            await auth.RevokeAsync(refreshToken, cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or VocabApiException
                                   || ex is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private async Task<VocabSession?> LoadLockedAsync(CancellationToken cancellationToken)
    {
        if (!_loaded)
        {
            _session = await store.LoadAsync(cancellationToken);
            _loaded = true;
        }
        return _session;
    }

    /// <summary>Makes <paramref name="session"/> the session in force, in memory, and numbers the change.</summary>
    private int SwapLocked(VocabSession? session)
    {
        _session = session;
        _loaded = true;
        return Interlocked.Increment(ref _version);
    }

    /// <summary>
    /// Writes the session in force to the store, with no cancellation. The memory swap has already happened, so a
    /// token auth.vo.cab has rotated can never be replayed from a stale in-memory session whatever the store does;
    /// the store's failure, if any, is returned for the caller to throw once its own work is complete.
    /// </summary>
    private async Task<ExceptionDispatchInfo?> TryPersistLockedAsync(VocabSession? session)
    {
        try
        {
            if (session is null)
                await store.ClearAsync(CancellationToken.None);
            else
                await store.SaveAsync(session, CancellationToken.None);
            return null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return ExceptionDispatchInfo.Capture(ex);
        }
    }

    private void RaiseSessionChanged(VocabSession? session, int version)
    {
        // A later change has already been announced (or is about to be): this one is history.
        if (Volatile.Read(ref _version) != version)
            return;
        SessionChanged?.Invoke(this, new VocabSessionChangedEventArgs(session));
    }

    private static string RandomToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool FixedTimeEquals(string left, string right) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));
}
