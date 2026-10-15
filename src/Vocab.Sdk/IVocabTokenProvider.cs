namespace Vocab.Sdk;

/// <summary>
/// Supplies the access token <see cref="VocabClient"/> sends. Platform packages implement this over the
/// signed-in user's session (and refresh behind it); <see cref="StaticTokenProvider"/> wraps a token you hold.
/// </summary>
public interface IVocabTokenProvider
{
    /// <summary>The current access token, or null when there is none (the call is then sent anonymously and refused).</summary>
    ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default);
}

/// <summary>An <see cref="IVocabTokenProvider"/> over one access token you already hold.</summary>
public sealed class StaticTokenProvider(string accessToken) : IVocabTokenProvider
{
    /// <inheritdoc />
    public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default) =>
        new(accessToken);
}
