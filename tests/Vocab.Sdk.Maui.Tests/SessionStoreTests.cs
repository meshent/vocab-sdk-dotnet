using Xunit;

namespace Vocab.Sdk.Maui.Tests;

public class SessionStoreTests
{
    [Fact]
    public async Task A_session_round_trips_through_secure_storage_under_the_configured_key()
    {
        var storage = new InMemorySecureStorage();
        var store = new SecureStorageSessionStore(storage, new VocabMauiOptions { StorageKey = "my.session" });
        var session = new VocabSession
        {
            AccessToken = "a1", RefreshToken = "r1", IdToken = "i1", Scope = "openid vo.profile.basic",
            ExpiresAt = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero),
        };

        await store.SaveAsync(session);

        Assert.Equal(["my.session"], storage.Entries.Keys);
        Assert.Equal(session, await store.LoadAsync());
    }

    [Fact]
    public async Task No_entry_is_no_session()
    {
        var store = new SecureStorageSessionStore(new InMemorySecureStorage(), new VocabMauiOptions());
        Assert.Null(await store.LoadAsync());
    }

    [Fact]
    public async Task An_entry_this_sdk_did_not_write_is_dropped_rather_than_thrown()
    {
        var storage = new InMemorySecureStorage();
        await storage.SetAsync("vocab.session", "not json");
        var store = new SecureStorageSessionStore(storage, new VocabMauiOptions());

        Assert.Null(await store.LoadAsync());
        Assert.Empty(storage.Entries);
    }

    [Fact]
    public async Task Clear_removes_only_the_session_entry()
    {
        var storage = new InMemorySecureStorage();
        await storage.SetAsync("other", "kept");
        var store = new SecureStorageSessionStore(storage, new VocabMauiOptions());
        await store.SaveAsync(new VocabSession { AccessToken = "a", ExpiresAt = DateTimeOffset.UnixEpoch });

        await store.ClearAsync();

        Assert.Equal(["other"], storage.Entries.Keys);
        Assert.Null(await store.LoadAsync());
    }

    [Fact]
    public void Options_refuse_a_redirect_that_is_not_an_absolute_uri()
    {
        Assert.Throws<InvalidOperationException>(() => new VocabMauiOptions { RedirectUri = "/callback" }.Validate());
        Assert.Throws<InvalidOperationException>(() => new VocabMauiOptions().Validate());
        new VocabMauiOptions { RedirectUri = "myapp://callback" }.Validate();
        new VocabMauiOptions { RedirectUri = "https://app.example/callback" }.Validate();
    }
}
