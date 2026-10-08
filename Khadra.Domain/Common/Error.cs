namespace Khadra.Domain.Common;

public enum ErrorKind
{
    Validation = 1,
    Unauthorized = 2,
    Forbidden = 3,
    NotFound = 4,
    Conflict = 5,
    Failure = 6,

    // A dependency the platform needs was not answering. Distinct from Failure because the caller
    // did nothing wrong and the same request may well succeed shortly -- 503, not 422.
    Unavailable = 7,

    // The caller must wait before asking again (429). Its RetryAfterSeconds extension becomes the
    // Retry-After header, the way the rate-limiting middleware answers.
    TooManyRequests = 8
}

// The single error currency across Domain, Application and API. `Code` is a stable machine code
// (e.g. "auth.invalid_credentials") that clients localise; `Message` is a user-safe English message.
//
// `Details` is for validation messages per field and travels under `errors`. `Extensions` is for a
// refusal that must say WHAT changed — the refund a cancellation would now return — and each entry
// travels top-level on the ProblemDetails, so a client never reads money as a validation message.
public sealed record Error(
    string Code,
    string Message,
    ErrorKind Kind,
    IReadOnlyDictionary<string, string[]>? Details = null,
    IReadOnlyDictionary<string, object?>? Extensions = null)
{
    public static Error ValidationDetails(IReadOnlyDictionary<string, string[]> details) =>
        new("validation.failed", "One or more fields are invalid.", ErrorKind.Validation, details);

    public static Error Validation(string code, string message) => new(code, message, ErrorKind.Validation);

    public static Error Unauthorized(string code, string message) => new(code, message, ErrorKind.Unauthorized);

    public static Error Forbidden(string code, string message) => new(code, message, ErrorKind.Forbidden);

    public static Error NotFound(string code, string message) => new(code, message, ErrorKind.NotFound);

    public static Error Conflict(string code, string message) => new(code, message, ErrorKind.Conflict);

    public static Error Failure(string code, string message) => new(code, message, ErrorKind.Failure);

    public static Error Unavailable(string code, string message) => new(code, message, ErrorKind.Unavailable);

    /// <summary>The extension a <see cref="ErrorKind.TooManyRequests"/> error carries its wait in, in whole seconds.</summary>
    public const string RetryAfterSecondsExtension = "retryAfterSeconds";

    /// <summary>A refusal to try again before <paramref name="retryAfter"/> has passed: 429 with Retry-After.</summary>
    public static Error TooManyRequests(string code, string message, TimeSpan retryAfter) =>
        new(
            code,
            message,
            ErrorKind.TooManyRequests,
            Extensions: new Dictionary<string, object?>
            {
                [RetryAfterSecondsExtension] = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds))
            });

    public override string ToString() => $"{Code}: {Message}";
}
