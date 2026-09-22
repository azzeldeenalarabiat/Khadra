namespace Khadra.Application.Common.Ports;

/// <summary>
/// Which builds of the customer app this API still serves, and where a customer gets a newer one.
/// </summary>
/// <remarks>
/// Raised when a release changes a contract an installed build cannot read. The server refuses older
/// builds itself (`MobileAppVersionGate`) because the builds that most need refusing are the ones
/// installed before this existed, which have no check of their own; `/app-config` publishes the same
/// figure so a build that DOES know can explain itself instead of failing call by call.
/// </remarks>
public interface IMobileAppPolicySettings
{
    /// <summary>The oldest build still served, or null when no build is refused.</summary>
    AppVersion? MinimumSupportedVersion { get; }

    /// <summary>Where the current build can be downloaded, or null when none has been published.</summary>
    Uri? UpdateUrl { get; }
}
