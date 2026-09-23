namespace Khadra.Application.Common.Ports;

/// <summary>One push to one phone.</summary>
/// <param name="Token">The push service's address for the install. Never logged.</param>
/// <param name="Title">The line the phone shows in bold.</param>
/// <param name="Body">The line under it.</param>
/// <param name="Data">What the app needs to open the right screen when the push is tapped.</param>
/// <param name="Tag">
/// The notification id. On Android a second push with the same tag REPLACES the first rather than
/// stacking beside it, which is what makes an at-least-once outbox look exactly-once on the phone.
/// </param>
public sealed record PushMessage(
    string Token,
    string Title,
    string Body,
    IReadOnlyDictionary<string, string> Data,
    string Tag)
{
    // A record's generated ToString prints every property, and the token must never reach a log.
    public override string ToString() => $"{nameof(PushMessage)} {{ Tag = {Tag} }}";
}

/// <summary>What the push service said about one message.</summary>
public sealed record PushSendResult
{
    private PushSendResult(bool accepted, bool tokenIsDead, string? error)
    {
        Accepted = accepted;
        TokenIsDead = tokenIsDead;
        Error = error;
    }

    /// <summary>The push service took it. Not proof the phone showed it.</summary>
    public bool Accepted { get; }

    /// <summary>The install behind this token is gone (uninstalled, data cleared). Stop sending to it.</summary>
    public bool TokenIsDead { get; }

    /// <summary>A short, token-free reason, for the outbox row and the log.</summary>
    public string? Error { get; }

    public static PushSendResult Delivered { get; } = new(true, false, null);

    public static PushSendResult DeadToken(string reason) => new(false, true, reason);

    /// <summary>Worth trying again later: a timeout, a 5xx, a rate limit, a credential hiccup.</summary>
    public static PushSendResult Transient(string reason) => new(false, false, reason);
}

/// <summary>The one place this platform talks to a push service.</summary>
public interface IPushSender
{
    /// <summary>False when no push provider is configured: every push is skipped, and startup says so.</summary>
    bool IsConfigured { get; }

    Task<PushSendResult> SendAsync(PushMessage message, CancellationToken cancellationToken = default);
}
