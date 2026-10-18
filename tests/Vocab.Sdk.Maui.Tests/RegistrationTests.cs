using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Authentication;
using Microsoft.Maui.Storage;
using Xunit;

namespace Vocab.Sdk.Maui.Tests;

public class RegistrationTests
{
    private static ServiceCollection Services()
    {
        var services = new ServiceCollection();
        // The platform singletons would throw off-device; register stand-ins first, as a test host or a custom store would.
        services.AddSingleton<ISecureStorage>(new InMemorySecureStorage());
        services.AddSingleton<IWebAuthenticator>(new NeverAuthenticator());
        return services;
    }

    [Fact]
    public void AddVocab_wires_sign_in_the_token_provider_and_the_clients_as_one_session()
    {
        var services = Services();
        services.AddVocab(
            vocab =>
            {
                vocab.NetworkId = "example";
                vocab.ClientId = "app";
            },
            signIn => signIn.RedirectUri = "vocabsample://callback");
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        var signIn = provider.GetRequiredService<VocabSignIn>();
        Assert.Same(signIn, provider.GetRequiredService<IVocabTokenProvider>());
        Assert.Same(signIn, provider.GetRequiredService<VocabSignIn>());
        Assert.NotNull(provider.GetRequiredService<VocabClient>());
        Assert.NotNull(provider.GetRequiredService<VocabAuthClient>());
        Assert.IsType<SecureStorageSessionStore>(provider.GetRequiredService<IVocabSessionStore>());
        Assert.IsType<WebAuthenticatorVocabAuthenticator>(provider.GetRequiredService<IVocabAuthenticator>());
        Assert.Equal("vocabsample://callback", provider.GetRequiredService<VocabMauiOptions>().RedirectUri);
        Assert.Equal(new Uri("https://example.api.vo.cab/"), provider.GetRequiredService<VocabOptions>().ApiBaseAddress);
    }

    [Fact]
    public void A_client_secret_is_refused_at_registration()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Services().AddVocab(
            vocab =>
            {
                vocab.NetworkId = "example";
                vocab.ClientId = "app";
                vocab.ClientSecret = "shh";
            },
            signIn => signIn.RedirectUri = "vocabsample://callback"));
        Assert.Contains("public client", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Options_are_validated_at_registration()
    {
        Assert.Throws<InvalidOperationException>(() => Services().AddVocab(vocab => vocab.ClientId = "app", signIn => signIn.RedirectUri = "vocabsample://callback"));
        Assert.Throws<InvalidOperationException>(() => Services().AddVocab(
            vocab =>
            {
                vocab.NetworkId = "example";
                vocab.ClientId = "app";
            },
            _ => { }));
    }

    [Fact]
    public void A_store_registered_first_wins()
    {
        var services = Services();
        var store = new RecordingStore();
        services.AddSingleton<IVocabSessionStore>(store);
        services.AddVocab(
            vocab =>
            {
                vocab.NetworkId = "example";
                vocab.ClientId = "app";
            },
            signIn => signIn.RedirectUri = "vocabsample://callback");
        using var provider = services.BuildServiceProvider();

        Assert.Same(store, provider.GetRequiredService<IVocabSessionStore>());
    }

    private sealed class NeverAuthenticator : IWebAuthenticator
    {
        public Task<WebAuthenticatorResult> AuthenticateAsync(WebAuthenticatorOptions webAuthenticatorOptions) => throw new NotSupportedException();
#if NET10_0_OR_GREATER
        public Task<WebAuthenticatorResult> AuthenticateAsync(WebAuthenticatorOptions webAuthenticatorOptions, CancellationToken cancellationToken) => throw new NotSupportedException();
#endif
    }

    private sealed class RecordingStore : IVocabSessionStore
    {
        public Task<VocabSession?> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult<VocabSession?>(null);
        public Task SaveAsync(VocabSession session, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ClearAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
