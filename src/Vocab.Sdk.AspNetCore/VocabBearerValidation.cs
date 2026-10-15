using System.Security.Claims;

namespace Vocab.Sdk.AspNetCore;

/// <summary>The checks <c>AddBearer</c> runs on a validated vo.cab token, beyond signature, issuer, audience and lifetime.</summary>
public static class VocabBearerValidation
{
    /// <summary>
    /// A vo.cab access token carries the client id it was minted for. Your API must accept only tokens minted for
    /// YOUR app: a token another network app obtained for the same user is not a credential for yours.
    /// </summary>
    public static bool IsMintedFor(ClaimsPrincipal principal, string clientId)
    {
        ArgumentNullException.ThrowIfNull(principal);
        var minted = principal.FindFirst(VocabClaims.ClientId)?.Value;
        return !string.IsNullOrEmpty(minted) && string.Equals(minted, clientId, StringComparison.Ordinal);
    }
}
