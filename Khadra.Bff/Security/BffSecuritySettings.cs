using System.ComponentModel.DataAnnotations;

namespace Khadra.Bff.Security;

internal sealed class BffSecuritySettings
{
    public const string SectionName = "BffSecurity";

    [Required, Url]
    public string ApiBaseUrl { get; init; } = null!;

    [Required]
    public string RedisConnection { get; init; } = "localhost:6379";

    // Hard ceiling for a dashboard session (Admin / Dealer staff do desk work; 8h covers a shift).
    [Range(1, 24)]
    public int SessionAbsoluteHours { get; init; } = 8;

    // Redis sliding expiry: the session ends after this much inactivity.
    [Range(5, 240)]
    public int SessionIdleMinutes { get; init; } = 30;
}
