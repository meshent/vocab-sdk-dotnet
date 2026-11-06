using System.Text.Json;
using Microsoft.Maui.Storage;

namespace Vocab.Sdk.Maui;

/// <summary>Where the session lives between launches. The default keeps it in the platform's secure storage.</summary>
public interface IVocabSessionStore
{
    /// <summary>The stored session, or null when there is none.</summary>
    Task<VocabSession?> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Stores <paramref name="session"/>, replacing whatever was there.</summary>
    Task SaveAsync(VocabSession session, CancellationToken cancellationToken = default);

    /// <summary>Forgets the stored session, if any.</summary>
    Task ClearAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Keeps the session as one JSON entry in <see cref="ISecureStorage"/> (the Keychain on Apple platforms, the
/// Keystore-backed encrypted preferences on Android, the credential locker on Windows), under
/// <see cref="VocabMauiOptions.StorageKey"/>.
/// </summary>
public sealed class SecureStorageSessionStore(ISecureStorage storage, VocabMauiOptions options) : IVocabSessionStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public async Task<VocabSession?> LoadAsync(CancellationToken cancellationToken = default)
    {
        var json = await storage.GetAsync(options.StorageKey);
        if (string.IsNullOrEmpty(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<VocabSession>(json, Json);
        }
        catch (JsonException)
        {
            // Not something this SDK wrote (or a shape it no longer reads): there is no session to speak of.
            storage.Remove(options.StorageKey);
            return null;
        }
    }

    /// <inheritdoc />
    public Task SaveAsync(VocabSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return storage.SetAsync(options.StorageKey, JsonSerializer.Serialize(session, Json));
    }

    /// <inheritdoc />
    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        storage.Remove(options.StorageKey);
        return Task.CompletedTask;
    }
}
