using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Vocab.Sdk.Internal;

namespace Vocab.Sdk;

/// <summary>
/// The network API for the signed-in user's profile in your network: <c>https://{network}.api.vo.cab/v1</c>.
/// Every call acts on the user the access token names, in the network the host names; nothing here takes a
/// profile, user or network id as a parameter, because the API accepts none.
/// </summary>
/// <remarks>
/// Construct it with an <see cref="HttpClient"/> whose <see cref="HttpClient.BaseAddress"/> is
/// <see cref="VocabOptions.ApiBaseAddress"/> (the platform packages register one for you) and an
/// <see cref="IVocabTokenProvider"/> for the user's access token. Errors are thrown as
/// <see cref="VocabApiException"/>; see <see cref="VocabErrorCodes"/>.
/// </remarks>
public sealed class VocabClient(HttpClient http, IVocabTokenProvider tokens)
{
    private const string V1 = "v1/";

    /// <summary>Confirms the token, your registration and the host agree, and tells you who the caller is here.</summary>
    /// <exception cref="VocabApiException"><see cref="VocabErrorCodes.NoProfile"/> when the user has no profile in your network.</exception>
    public async Task<WhoAmI> WhoAmIAsync(CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get, V1 + "whoami", content: null, cancellationToken);
        return await VocabJson.ReadAsync<WhoAmI>(response, cancellationToken);
    }

    /// <summary>Records your app's own id for the user's profile in your network (an upsert).</summary>
    public async Task<ExternalIdLink> SetExternalIdAsync(string externalId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalId);
        using var response = await SendAsync(HttpMethod.Put, V1 + "me/external-id",
            JsonContent.Create(new { externalId }, options: VocabJson.Options), cancellationToken);
        return await VocabJson.ReadAsync<ExternalIdLink>(response, cancellationToken);
    }

    /// <summary>The id your app recorded for this profile, or null when it has recorded none.</summary>
    public async Task<ExternalIdLink?> GetExternalIdAsync(CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get, V1 + "me/external-id", content: null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            var (code, message) = await VocabJson.ReadErrorAsync(response, cancellationToken);
            if (code == VocabErrorCodes.NotFound)
                return null;
            throw new VocabApiException(response.StatusCode, code, message);
        }
        return await VocabJson.ReadAsync<ExternalIdLink>(response, cancellationToken);
    }

    /// <summary>Stops asserting your app's id for this profile. Returns false when there was none.</summary>
    public async Task<bool> ClearExternalIdAsync(CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Delete, V1 + "me/external-id", content: null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            var (code, message) = await VocabJson.ReadErrorAsync(response, cancellationToken);
            if (code == VocabErrorCodes.NotFound)
                return false;
            throw new VocabApiException(response.StatusCode, code, message);
        }
        await VocabJson.EnsureSuccessAsync(response, cancellationToken);
        return true;
    }

    /// <summary>Edits the user's profile in your network (requires <see cref="VocabScopes.ProfileWrite"/>).</summary>
    /// <exception cref="ArgumentException">No field of <paramref name="update"/> is set.</exception>
    public async Task UpdateProfileAsync(ProfileUpdate update, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        var fields = update.ToFields();
        if (fields.Count == 0)
            throw new ArgumentException("Set at least one field of the update.", nameof(update));

        using var response = await SendAsync(HttpMethod.Patch, V1 + "me/profile",
            JsonContent.Create(fields, options: VocabJson.Options), cancellationToken);
        await VocabJson.EnsureSuccessAsync(response, cancellationToken);
    }

    /// <summary>Renames the user's profile in your network. Returns the handle as stored, which may be folded.</summary>
    /// <exception cref="VocabApiException"><see cref="VocabErrorCodes.HandleUnavailable"/> when the handle is taken or reserved.</exception>
    public async Task<string> SetHandleAsync(string handle, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(handle);
        using var response = await SendAsync(HttpMethod.Put, V1 + "me/profile/handle",
            JsonContent.Create(new { handle }, options: VocabJson.Options), cancellationToken);
        var body = await VocabJson.ReadAsync<HandleResponse>(response, cancellationToken);
        return body.Handle;
    }

    /// <summary>
    /// Uploads a new avatar image (JPEG, PNG or WebP, at most a few megabytes) and applies it to the user's
    /// profile. Requires <see cref="VocabScopes.ProfileWrite"/>.
    /// </summary>
    public async Task<AvatarUpload> UploadAvatarAsync(Stream image, string contentType, string fileName = "avatar", CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);

        using var form = new MultipartFormDataContent();
        var part = new StreamContent(image);
        part.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(part, "file", fileName);

        using var response = await SendAsync(HttpMethod.Post, V1 + "me/profile/avatar", form, cancellationToken);
        return await VocabJson.ReadAsync<AvatarUpload>(response, cancellationToken);
    }

    /// <summary>
    /// Asks vo.cab to add an email address to the user's account. The address is added only after the user
    /// confirms vo.cab's own mail, and never becomes their sign-in address. Requires <see cref="VocabScopes.ProfileWrite"/>.
    /// </summary>
    public async Task<EmailAddition> AddEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        using var response = await SendAsync(HttpMethod.Post, V1 + "me/email",
            JsonContent.Create(new { email }, options: VocabJson.Options), cancellationToken);
        return await VocabJson.ReadAsync<EmailAddition>(response, cancellationToken);
    }

    /// <summary>The profiles in your network that have shared their basic profile with this user (requires <see cref="VocabScopes.ProfileLinked"/>).</summary>
    public async Task<IReadOnlyList<VocabProfile>> ListLinkedProfilesAsync(CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get, V1 + "me/linked-profiles", content: null, cancellationToken);
        var body = await VocabJson.ReadAsync<LinkedProfilesResponse>(response, cancellationToken);
        return body.Profiles;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, HttpContent? content, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (await tokens.GetAccessTokenAsync(cancellationToken) is { Length: > 0 } token)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private sealed record HandleResponse(string Handle);
}
