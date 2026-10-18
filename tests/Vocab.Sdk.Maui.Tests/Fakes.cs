using System.Collections.Concurrent;
using Microsoft.Maui.Storage;

namespace Vocab.Sdk.Maui.Tests;

/// <summary>Secure storage without a device: a dictionary.</summary>
internal sealed class InMemorySecureStorage : ISecureStorage
{
    public ConcurrentDictionary<string, string> Entries { get; } = new(StringComparer.Ordinal);

    public Task<string?> GetAsync(string key) => Task.FromResult(Entries.TryGetValue(key, out var value) ? value : null);

    public Task SetAsync(string key, string value)
    {
        Entries[key] = value;
        return Task.CompletedTask;
    }

    public bool Remove(string key) => Entries.TryRemove(key, out _);

    public void RemoveAll() => Entries.Clear();
}

/// <summary>A browser that answers at once with whatever the test scripted, remembering where it was sent.</summary>
internal sealed class FakeAuthenticator(Func<Uri, IReadOnlyDictionary<string, string>> answer) : IVocabAuthenticator
{
    public Uri? AuthorizeUrl { get; private set; }
    public Uri? CallbackUrl { get; private set; }

    /// <summary>Answers like auth.vo.cab does on success: the state it was given and a code.</summary>
    public static FakeAuthenticator Succeeding(string code = "the-code") =>
        new(url => new Dictionary<string, string>(StringComparer.Ordinal) { ["code"] = code, ["state"] = Query(url)["state"] });

    public Task<IReadOnlyDictionary<string, string>> AuthenticateAsync(Uri authorizeUrl, Uri callbackUrl, CancellationToken cancellationToken = default)
    {
        AuthorizeUrl = authorizeUrl;
        CallbackUrl = callbackUrl;
        return Task.FromResult(answer(authorizeUrl));
    }

    public static Dictionary<string, string> Query(Uri url) =>
        url.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .ToDictionary(p => Uri.UnescapeDataString(p[0]), p => p.Length > 1 ? Uri.UnescapeDataString(p[1]) : string.Empty, StringComparer.Ordinal);
}

internal sealed class Clock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>Everything a VocabSignIn needs, wired to fakes, with the pieces exposed for assertions.</summary>
internal sealed class Harness
{
    public static readonly Uri Authority = new("https://auth.example.test");

    public FakeAuthority Server { get; } = new();
    public InMemorySecureStorage Storage { get; } = new();
    public Clock Clock { get; } = new();
    public VocabOptions Options { get; } = new() { NetworkId = "example", ClientId = "app", Authority = Authority };
    public VocabMauiOptions SignInOptions { get; } = new() { RedirectUri = "vocabsample://callback" };
    public FakeAuthenticator Authenticator { get; set; } = FakeAuthenticator.Succeeding();
    public List<VocabSession?> Changes { get; } = [];

    public VocabAuthClient Auth => new(new HttpClient(Server) { BaseAddress = Authority }, Options);

    public SecureStorageSessionStore Store => new(Storage, SignInOptions);

    public VocabSignIn Build()
    {
        var signIn = new VocabSignIn(Auth, Store, Authenticator, SignInOptions, Clock);
        signIn.SessionChanged += (_, e) => Changes.Add(e.Session);
        return signIn;
    }

    /// <summary>Puts a session in storage as a previous launch would have left it.</summary>
    public async Task<VocabSession> StoreSessionAsync(TimeSpan untilExpiry, string? refreshToken = "r1")
    {
        var session = new VocabSession { AccessToken = "a1", RefreshToken = refreshToken, ExpiresAt = Clock.Now + untilExpiry };
        await Store.SaveAsync(session);
        return session;
    }

    public static string Tokens(string access, string? refresh = "r2", int expiresIn = 3600) =>
        refresh is null
            ? $$$"""{"access_token":"{{{access}}}","token_type":"Bearer","expires_in":{{{expiresIn}}}}"""
            : $$$"""{"access_token":"{{{access}}}","token_type":"Bearer","expires_in":{{{expiresIn}}},"refresh_token":"{{{refresh}}}"}""";
}
