using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;

namespace Vocab.Sdk.AspNetCore;

/// <summary>
/// Wraps each token handler of the <c>AddBearer</c> scheme so a token that validates is also required to have been
/// minted for this app. The check lives in <c>JwtBearerOptions.TokenHandlers</c>, not in the events, so neither a
/// replaced <c>Events</c> object nor an <c>EventsType</c> resolved from DI can bypass it.
/// </summary>
internal sealed class MintedForThisAppTokenHandler(TokenHandler inner, string clientId) : TokenHandler
{
    /// <summary>The handler that does the actual validation (signature, issuer, audience, lifetime).</summary>
    public TokenHandler Inner => inner;

    /// <inheritdoc />
    public override int MaximumTokenSizeInBytes
    {
        get => inner.MaximumTokenSizeInBytes;
        set => inner.MaximumTokenSizeInBytes = value;
    }

    /// <inheritdoc />
    public override SecurityToken ReadToken(string token) => inner.ReadToken(token);

    /// <inheritdoc />
    public override async Task<TokenValidationResult> ValidateTokenAsync(string token, TokenValidationParameters validationParameters) =>
        RequireMintedForThisApp(await inner.ValidateTokenAsync(token, validationParameters));

    /// <inheritdoc />
    public override async Task<TokenValidationResult> ValidateTokenAsync(SecurityToken token, TokenValidationParameters validationParameters) =>
        RequireMintedForThisApp(await inner.ValidateTokenAsync(token, validationParameters));

    private TokenValidationResult RequireMintedForThisApp(TokenValidationResult result)
    {
        if (!result.IsValid)
            return result;
        if (result.ClaimsIdentity is { } identity && VocabBearerValidation.IsMintedFor(new ClaimsPrincipal(identity), clientId))
            return result;
        return new TokenValidationResult
        {
            IsValid = false,
            Exception = new SecurityTokenException("The access token was not minted for this app."),
        };
    }
}
