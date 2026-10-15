namespace Vocab.Sdk;

/// <summary>
/// What the SDK needs to know about your app: which network it belongs to and how it was registered in the
/// network's Apps pane on vo.cab. Everything else (hosts, audience) has a production default.
/// </summary>
public sealed class VocabOptions
{
    /// <summary>The network your app is registered in: the label of <c>{network}.vo.cab</c>.</summary>
    public string NetworkId { get; set; } = string.Empty;

    /// <summary>The client id the Apps pane issued when you registered the app.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// The client secret, for a confidential app only (a server-rendered web app or an API). Leave it null for
    /// a public app (mobile, desktop, single-page): auth.vo.cab refuses a secret from a public client.
    /// </summary>
    public string? ClientSecret { get; set; }

    /// <summary>The sign-in server. Change it only when pointing at a test deployment.</summary>
    public Uri Authority { get; set; } = new("https://auth.vo.cab");

    /// <summary>The network API host suffix; your network's API is <c>https://{NetworkId}.{ApiHostSuffix}</c>.</summary>
    public string ApiHostSuffix { get; set; } = "api.vo.cab";

    /// <summary>
    /// The audience every access token minted for a network app carries, and the value an API that accepts those
    /// tokens must require. This is the network API's identity, not your app's.
    /// </summary>
    public string ApiAudience { get; set; } = "network.api.vo.cab";

    /// <summary>The base address of your network's API, derived from <see cref="NetworkId"/> and <see cref="ApiHostSuffix"/>.</summary>
    public Uri ApiBaseAddress
    {
        get
        {
            var suffix = ApiHostSuffix.Trim().Trim('.');
            var scheme = suffix.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                         || suffix.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
                ? "http"
                : "https";
            return new Uri($"{scheme}://{NetworkId}.{suffix}/");
        }
    }

    /// <summary>Throws when the options cannot identify an app. Called by every entry point before use.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(NetworkId) || NetworkId.Contains('.', StringComparison.Ordinal))
            throw new InvalidOperationException("VocabOptions.NetworkId must be the network's label (one DNS label, no dots).");
        if (string.IsNullOrWhiteSpace(ClientId))
            throw new InvalidOperationException("VocabOptions.ClientId is required: copy it from the network's Apps pane on vo.cab.");
        if (!Authority.IsAbsoluteUri)
            throw new InvalidOperationException("VocabOptions.Authority must be an absolute URL.");
        if (string.IsNullOrWhiteSpace(ApiHostSuffix))
            throw new InvalidOperationException("VocabOptions.ApiHostSuffix is required.");
    }
}
