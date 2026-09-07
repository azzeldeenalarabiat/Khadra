namespace Khadra.Application.Common.Ports;

/// <summary>
/// How long an issued access token stays valid.
/// </summary>
/// <remarks>
/// Read by the security screen so it can say how long a revoked session can still make requests.
/// Revoking a refresh-token family stops it being refreshed but does not rotate the security stamp,
/// so a token already in flight lives out its life. The number is configuration; a console that
/// printed "15 minutes" as a literal would go on saying it after the setting moved.
/// </remarks>
public interface IAccessTokenSettings
{
    int AccessTokenMinutes { get; }
}
