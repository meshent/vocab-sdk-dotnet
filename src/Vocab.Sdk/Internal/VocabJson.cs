using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vocab.Sdk.Internal;

internal static class VocabJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Reads a success body, or throws the API's error as a <see cref="VocabApiException"/>.</summary>
    public static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await EnsureSuccessAsync(response, cancellationToken);
        var value = await response.Content.ReadFromJsonAsync<T>(Options, cancellationToken);
        return value ?? throw new VocabApiException(response.StatusCode, VocabErrorCodes.Unknown, "The API answered with an empty body.");
    }

    /// <summary>Throws the API's <c>{ error, code }</c> body as a <see cref="VocabApiException"/> on a non-success status.</summary>
    public static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var (code, message) = await ReadErrorAsync(response, cancellationToken);
        throw new VocabApiException(response.StatusCode, code, message, RetryAfter(response.Headers));
    }

    public static async Task<(string Code, string Message)> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        ApiError? error = null;
        if (response.Content.Headers.ContentType?.MediaType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true)
        {
            try
            {
                error = await response.Content.ReadFromJsonAsync<ApiError>(Options, cancellationToken);
            }
            catch (JsonException)
            {
                // Not the API's shape; fall through to the status-derived default.
            }
        }

        var code = error?.Code ?? DefaultCode(response.StatusCode);
        var message = error?.Error ?? $"The API answered {(int)response.StatusCode} {response.ReasonPhrase}.";
        return (code, message);
    }

    private static string DefaultCode(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized => VocabErrorCodes.InvalidToken,
        HttpStatusCode.NotFound => VocabErrorCodes.NotFound,
        HttpStatusCode.TooManyRequests => VocabErrorCodes.RateLimited,
        HttpStatusCode.BadRequest => VocabErrorCodes.InvalidRequest,
        _ => VocabErrorCodes.Unknown,
    };

    private static TimeSpan? RetryAfter(HttpResponseHeaders headers)
    {
        if (headers.RetryAfter is not { } retry)
            return null;
        if (retry.Delta is { } delta)
            return delta;
        if (retry.Date is { } date)
            return date - DateTimeOffset.UtcNow is var wait && wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
        return null;
    }
}
