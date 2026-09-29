using System.ComponentModel.DataAnnotations;
using Khadra.Application.Payables.ReadModels;
using Microsoft.Extensions.Options;

namespace Khadra.Infrastructure.Configuration;

/// <summary>
/// How the settlement pass paces the office payables ledger (payments Phase 8). None of it changes a figure: what a
/// booking comes to for its office is the calculator's, and these only say when and how much to look.
/// </summary>
public sealed class PayablesOptions
{
    public const string SectionName = "Payables";

    /// <summary>
    /// How long after an outcome became final the pass waits before recording it, so a fact committed just after it
    /// with an earlier instant is read too.
    /// </summary>
    [Range(1, 1_440)]
    public int FinalityMarginMinutes { get; init; } = 10;

    /// <summary>The most bookings one pass records or holds; the rest wait for the next pass.</summary>
    [Range(1, 1_000)]
    public int MaxBookingsPerPass { get; init; } = 100;

    /// <summary>The most open payables one pass checks against their records again; the next pass takes the next page.</summary>
    [Range(1, 1_000)]
    public int MaxVerificationsPerPass { get; init; } = 50;

    /// <summary>The first wait before a held booking is looked at again; each further look doubles it.</summary>
    [Range(1, 86_400)]
    public int RetryInitialSeconds { get; init; } = 300;

    /// <summary>The longest wait between two looks at a held booking.</summary>
    [Range(1, 604_800)]
    public int RetryMaxSeconds { get; init; } = 21_600;
}

internal sealed class PayablesSettings(IOptions<PayablesOptions> options) : IPayablesSettings
{
    public TimeSpan FinalityMargin => TimeSpan.FromMinutes(options.Value.FinalityMarginMinutes);

    public int MaxBookingsPerPass => options.Value.MaxBookingsPerPass;

    public int MaxVerificationsPerPass => options.Value.MaxVerificationsPerPass;

    public TimeSpan RetryInitialDelay => TimeSpan.FromSeconds(options.Value.RetryInitialSeconds);

    public TimeSpan RetryMaxDelay => TimeSpan.FromSeconds(options.Value.RetryMaxSeconds);
}
