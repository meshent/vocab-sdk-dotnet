using System.Security.Cryptography;
using System.Text;

namespace Vocab.Sdk;

/// <summary>Proof Key for Code Exchange (RFC 7636), S256. auth.vo.cab requires it for every app.</summary>
public static class Pkce
{
    /// <summary>A fresh verifier and its S256 challenge. Keep the verifier until the code is redeemed.</summary>
    public static (string Verifier, string Challenge) Create()
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        return (verifier, ChallengeFor(verifier));
    }

    /// <summary>The S256 challenge for a verifier: base64url(SHA-256(verifier)).</summary>
    public static string ChallengeFor(string verifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(verifier);
        return Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
