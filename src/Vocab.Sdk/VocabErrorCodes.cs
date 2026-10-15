namespace Vocab.Sdk;

/// <summary>
/// The <c>code</c> values the network API and auth.vo.cab return in their error body <c>{ error, code }</c>.
/// Compare <see cref="VocabApiException.Code"/> against these rather than against HTTP status codes.
/// </summary>
public static class VocabErrorCodes
{
    /// <summary>400: the request was malformed or named a field the route does not accept.</summary>
    public const string InvalidRequest = "invalid_request";

    /// <summary>401: the access token is missing, malformed, expired, or not minted for this API.</summary>
    public const string InvalidToken = "invalid_token";

    /// <summary>400 from auth.vo.cab: the authorization code or refresh token is spent, revoked or not this app's.</summary>
    public const string InvalidGrant = "invalid_grant";

    /// <summary>403: the token does not carry the scope this call requires.</summary>
    public const string InsufficientScope = "insufficient_scope";

    /// <summary>403: the network does not hold the entitlement this call requires.</summary>
    public const string LicenseRequired = "license_required";

    /// <summary>403: your app's registration is not active in this network.</summary>
    public const string AppSuspended = "app_suspended";

    /// <summary>403: the user's account is restricted to read-only access.</summary>
    public const string AccountRestricted = "account_restricted";

    /// <summary>404: the resource does not exist for this caller.</summary>
    public const string NotFound = "not_found";

    /// <summary>404: the token is valid but the user has no profile in this network.</summary>
    public const string NoProfile = "no_profile";

    /// <summary>409: the requested handle is not available.</summary>
    public const string HandleUnavailable = "handle_unavailable";

    /// <summary>409: the email address was refused.</summary>
    public const string EmailRefused = "email_refused";

    /// <summary>413: the upload is larger than the API accepts.</summary>
    public const string PayloadTooLarge = "payload_too_large";

    /// <summary>415: the upload's content type is not accepted.</summary>
    public const string UnsupportedMediaType = "unsupported_media_type";

    /// <summary>422: the image could not be processed.</summary>
    public const string UnprocessableImage = "unprocessable_image";

    /// <summary>429: slow down; <see cref="VocabApiException.RetryAfter"/> says how long.</summary>
    public const string RateLimited = "rate_limited";

    /// <summary>503: the email verification service is unavailable; retry later.</summary>
    public const string VerificationUnavailable = "verification_unavailable";

    /// <summary>The SDK's own code for a response with no readable error body.</summary>
    public const string Unknown = "unknown";
}
