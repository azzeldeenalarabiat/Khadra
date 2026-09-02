namespace Khadra.Domain.Common;

public enum ErrorKind
{
    Validation = 1,
    Unauthorized = 2,
    Forbidden = 3,
    NotFound = 4,
    Conflict = 5,
    Failure = 6
}

// The single error currency across Domain, Application and API. `Code` is a stable machine code
// (e.g. "auth.invalid_credentials") that clients localise; `Message` is a user-safe English message.
public sealed record Error(
    string Code,
    string Message,
    ErrorKind Kind,
    IReadOnlyDictionary<string, string[]>? Details = null)
{
    public static Error ValidationDetails(IReadOnlyDictionary<string, string[]> details) =>
        new("validation.failed", "One or more fields are invalid.", ErrorKind.Validation, details);

    public static Error Validation(string code, string message) => new(code, message, ErrorKind.Validation);

    public static Error Unauthorized(string code, string message) => new(code, message, ErrorKind.Unauthorized);

    public static Error Forbidden(string code, string message) => new(code, message, ErrorKind.Forbidden);

    public static Error NotFound(string code, string message) => new(code, message, ErrorKind.NotFound);

    public static Error Conflict(string code, string message) => new(code, message, ErrorKind.Conflict);

    public static Error Failure(string code, string message) => new(code, message, ErrorKind.Failure);

    public override string ToString() => $"{Code}: {Message}";
}
