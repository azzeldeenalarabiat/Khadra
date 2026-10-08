using System.ComponentModel.DataAnnotations;

namespace Khadra.Infrastructure.Configuration;

/// <summary>
/// The per-account ceiling on failed sign-ins (pre-launch item 51; owner, 2026-10-08): this many failures of one
/// account name within the window, wherever they come from, refuse it for the block. Separate from, and in addition
/// to, the address-and-name rate limit on the sign-in route.
/// </summary>
public sealed class SignInThrottleOptions
{
    public const string SectionName = "Authentication:SignInThrottle";

    [Range(3, 100)]
    public int MaxFailures { get; init; } = 8;

    [Range(1, 1440)]
    public int WindowMinutes { get; init; } = 15;

    [Range(1, 1440)]
    public int BlockMinutes { get; init; } = 15;
}
