namespace Vocab.Sdk.Maui;

/// <summary>
/// Sign-in did not produce a session: auth.vo.cab sent the user back with an error (the user declined, or the
/// app's registration does not match), or the callback did not pass the SDK's checks.
/// </summary>
public sealed class VocabSignInException : Exception
{
    /// <summary>Creates the exception with no detail.</summary>
    public VocabSignInException()
        : this(VocabErrorCodes.Unknown, "Sign-in did not complete.")
    {
    }

    /// <summary>Creates the exception from a message alone.</summary>
    public VocabSignInException(string message)
        : this(VocabErrorCodes.Unknown, message)
    {
    }

    /// <summary>Creates the exception from a message and the failure that caused it.</summary>
    public VocabSignInException(string message, Exception innerException)
        : base(message, innerException)
    {
        Code = VocabErrorCodes.Unknown;
    }

    /// <summary>Creates the exception from auth.vo.cab's error code and description.</summary>
    public VocabSignInException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    /// <summary>
    /// The error: auth.vo.cab's <c>error</c> parameter (<c>access_denied</c> when the user declined), or
    /// <see cref="InvalidState"/> when the callback did not match the sign-in the SDK started.
    /// </summary>
    public string Code { get; }

    /// <summary>The callback's <c>state</c> was missing or not the one this sign-in sent: the response is not trusted.</summary>
    public const string InvalidState = "invalid_state";

    /// <summary>The callback carried neither a code nor an error.</summary>
    public const string MissingCode = "missing_code";

    /// <summary>True when the user chose not to sign in.</summary>
    public bool IsAccessDenied => Code == "access_denied";
}
