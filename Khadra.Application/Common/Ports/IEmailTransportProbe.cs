namespace Khadra.Application.Common.Ports;

/// <summary>
/// What the platform found when it asked the mail service whether it would accept mail.
/// </summary>
/// <param name="IsConfigured">False when no credentials are set, which is not a failure — just unfinished setup.</param>
/// <param name="CanConnect">The host answered on the configured port.</param>
/// <param name="CanAuthenticate">The service accepted the credentials, so mail will actually go out.</param>
/// <param name="Description">One sentence an operator can act on, including the provider's own words when it refused.</param>
public sealed record EmailTransportStatus(
    bool IsConfigured,
    bool CanConnect,
    bool CanAuthenticate,
    string Description)
{
    /// <summary>Whether a message sent right now would be accepted for delivery.</summary>
    public bool IsReady => IsConfigured && CanConnect && CanAuthenticate;
}

/// <summary>
/// Asks the mail service whether it is reachable and whether it will accept our credentials —
/// without sending anything to anybody.
///
/// It exists because "is email working?" was previously only answerable by registering an account
/// and waiting to see whether anything arrived. A platform whose sign-up depends on mail should be
/// able to say, at startup and on demand, whether mail is going to work.
/// </summary>
public interface IEmailTransportProbe
{
    Task<EmailTransportStatus> CheckAsync(CancellationToken cancellationToken = default);
}
