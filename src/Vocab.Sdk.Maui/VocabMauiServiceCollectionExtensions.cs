using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Maui.Authentication;
using Microsoft.Maui.Storage;

namespace Vocab.Sdk.Maui;

/// <summary>Registers the Vocab SDK with a .NET MAUI app's services.</summary>
public static class VocabMauiServiceCollectionExtensions
{
    /// <summary>
    /// Sign in with vo.cab for a .NET MAUI app. Registers <see cref="VocabSignIn"/> (also as the
    /// <see cref="IVocabTokenProvider"/>), a <see cref="VocabClient"/> for your network's API that sends the
    /// signed-in user's token, and a <see cref="VocabAuthClient"/> for auth.vo.cab, over the platform's secure
    /// storage and system browser.
    /// </summary>
    /// <param name="services">Your app's services (<c>builder.Services</c> in <c>MauiProgram</c>).</param>
    /// <param name="configure">Your app's registration: <see cref="VocabOptions.NetworkId"/> and <see cref="VocabOptions.ClientId"/>. Leave the secret null; a mobile app is a public client.</param>
    /// <param name="configureSignIn">How sign-in behaves: at least <see cref="VocabMauiOptions.RedirectUri"/>.</param>
    /// <remarks>
    /// Every registration uses <c>TryAdd</c>, so a service you register before calling this (your own
    /// <see cref="IVocabSessionStore"/>, <see cref="ISecureStorage"/>, <see cref="IWebAuthenticator"/>,
    /// <see cref="TimeProvider"/> or <see cref="VocabAuthClient"/>) wins. Both option sets are validated here, so a
    /// misconfigured app fails at startup rather than at the first sign-in.
    /// </remarks>
    public static IServiceCollection AddVocab(this IServiceCollection services, Action<VocabOptions> configure, Action<VocabMauiOptions> configureSignIn)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        ArgumentNullException.ThrowIfNull(configureSignIn);

        var options = new VocabOptions();
        configure(options);
        options.Validate();
        if (!string.IsNullOrEmpty(options.ClientSecret))
            throw new InvalidOperationException("A MAUI app is a public client: leave VocabOptions.ClientSecret null. A secret shipped in an app binary is not secret, and auth.vo.cab refuses one from a public app.");

        var signIn = new VocabMauiOptions();
        configureSignIn(signIn);
        signIn.Validate();

        services.TryAddSingleton(options);
        services.TryAddSingleton(signIn);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(_ => SecureStorage.Default);
        services.TryAddSingleton(_ => WebAuthenticator.Default);
        services.TryAddSingleton<IVocabSessionStore, SecureStorageSessionStore>();
        services.TryAddSingleton<IVocabAuthenticator, WebAuthenticatorVocabAuthenticator>();
#pragma warning disable CA2000 // The HttpClients live as long as the singletons that own them: the app's lifetime.
        services.TryAddSingleton(sp => new VocabAuthClient(new HttpClient { BaseAddress = options.Authority }, sp.GetRequiredService<VocabOptions>()));
        services.TryAddSingleton<VocabSignIn>();
        services.TryAddSingleton<IVocabTokenProvider>(sp => sp.GetRequiredService<VocabSignIn>());
        services.TryAddSingleton(sp => new VocabClient(new HttpClient { BaseAddress = options.ApiBaseAddress }, sp.GetRequiredService<IVocabTokenProvider>()));
#pragma warning restore CA2000
        return services;
    }
}
