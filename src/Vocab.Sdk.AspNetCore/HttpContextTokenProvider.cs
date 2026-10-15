using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Vocab.Sdk.AspNetCore;

/// <summary>
/// The access token for the current request: the signed-in session's token after <c>AddSignIn</c> (refreshed by the
/// session when it nears expiry), or the bearer token the caller presented after <c>AddBearer</c>.
/// </summary>
public sealed class HttpContextTokenProvider(IHttpContextAccessor accessor) : IVocabTokenProvider
{
    /// <inheritdoc />
    public async ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        var http = accessor.HttpContext;
        if (http is null)
            return null;

        // A token the caller presented to THIS API is forwarded as is: the user is acting through the app.
        var header = http.Request.Headers[HeaderNames.Authorization].ToString();
        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return header["Bearer ".Length..].Trim();

        // Otherwise the signed-in session's token, stored by AddSignIn (SaveTokens).
        if (http.User.Identity?.IsAuthenticated == true)
            return await http.GetTokenAsync(VocabDefaults.CookieScheme, "access_token");

        return null;
    }
}
