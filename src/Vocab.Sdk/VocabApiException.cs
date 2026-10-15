using System.Net;

namespace Vocab.Sdk;

/// <summary>
/// A non-success answer from auth.vo.cab or the network API. <see cref="Code"/> is the API's machine-readable
/// error code (see <see cref="VocabErrorCodes"/>); <see cref="Exception.Message"/> is its human-readable message.
/// </summary>
public sealed class VocabApiException : Exception
{
    /// <summary>Creates the exception from the API's status, error code, message and optional Retry-After wait.</summary>
    public VocabApiException(HttpStatusCode statusCode, string code, string message, TimeSpan? retryAfter = null)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
        RetryAfter = retryAfter;
    }

    /// <summary>The HTTP status the API answered with.</summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>The API's error code, one of <see cref="VocabErrorCodes"/>.</summary>
    public string Code { get; }

    /// <summary>For a <see cref="VocabErrorCodes.RateLimited"/> answer: how long to wait before retrying.</summary>
    public TimeSpan? RetryAfter { get; }

    /// <summary>True when the failure is the token's (missing, expired, wrong audience): refresh or sign in again.</summary>
    public bool IsTokenProblem => Code == VocabErrorCodes.InvalidToken;

    /// <summary>True when the user has no profile in your network: send them through sign-in so they can create one.</summary>
    public bool IsNoProfile => Code == VocabErrorCodes.NoProfile;
}
