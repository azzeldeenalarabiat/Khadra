using System.Net;

namespace Khadra.Bff.Security;

/// <summary>
/// The session's access token needed refreshing and the API could not answer just now: a 429, a 5xx, the
/// 503 <c>auth.refresh_conflict</c> of a refresh race, a timeout, a dropped connection, an answer that was not the
/// token pair, or a 401/403 that did not come from the API itself (pre-launch item 246).
/// </summary>
/// <remarks>
/// None of those says the session is over, so the session is kept: its cookie and its stored refresh token are left
/// exactly as they were, and the browser is answered 503 with the API's <c>Retry-After</c> where it gave one
/// (<see cref="BffExceptionHandler"/>). The next request asks again. Only a 401 or 403 that is the API's own refusal
/// ends a session, which is the rule the customer app has kept since 1.1.0. Until 2026-10-10 the BFFs ended one on any
/// failure, and a website session was lost on Staging when the refresh reached a sleeping API and Render answered 429.
/// </remarks>
public sealed class BffSessionRefreshUnavailableException(
    TimeSpan? retryAfter,
    HttpStatusCode? status = null,
    string? code = null,
    Exception? innerException = null)
    : Exception(
        $"The session could not be refreshed just now ({(status is { } answered ? $"{(int)answered}" : "no answer")}{(code is null ? string.Empty : $" {code}")}).",
        innerException)
{
    /// <summary>How long the API asked the caller to wait, when it said; null otherwise.</summary>
    public TimeSpan? RetryAfter { get; } = retryAfter;

    /// <summary>The status the refresh was answered with; null when there was no answer (a timeout, a dropped connection).</summary>
    public HttpStatusCode? Status { get; } = status;

    /// <summary>The API's error code, when the answer was the API's ProblemDetails.</summary>
    public string? Code { get; } = code;
}
