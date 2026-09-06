using Khadra.Domain.Common;

namespace Khadra.Domain.PlatformSettings;

public static class PlatformSettingsErrors
{
    /// <remarks>
    /// Two entries reading the same thing are indistinguishable in every dropdown and in customer
    /// search, and there is no delete — only retire — so the mistake would be permanent. Only the
    /// OFFERED entries are compared: a retired name is out of every list and may be reused.
    /// </remarks>
    public static readonly Error LookupNameTaken =
        Error.Conflict("lookup.name_taken", "Another entry on this list already uses that name.");

    public static readonly Error PenaltyRangeInverted =
        Error.Validation("settings.penalty_range_inverted", "The maximum non-delivery penalty must be at least the minimum.");

    public static readonly Error InvalidNoShowTimeout =
        Error.Validation("settings.invalid_no_show_timeout", "The no-show timeout must be between 1 and 720 hours.");

    public static readonly Error InvalidCancellationWindow =
        Error.Validation("settings.invalid_cancellation_window", "The free cancellation window must be between 0 minutes and 7 days.");

    public static readonly Error InvalidSla =
        Error.Validation("settings.invalid_sla", "The admin SLA must be between 1 and 720 hours.");

    public static readonly Error InvalidMinimumAge =
        Error.Validation("settings.invalid_minimum_age", "The minimum renter age must be between 18 and 30.");

    public static readonly Error DepositMustCoverCommission =
        Error.Validation("settings.deposit_below_commission", "The deposit percentage must be at least the commission percentage, otherwise the platform cannot collect its commission from the card deposit.");

    public static readonly Error InvalidLookupName =
        Error.Validation("lookup.invalid_name", "Both the English and Arabic names are required and must be at most 100 characters.");

    public static readonly Error LookupAlreadyActive =
        Error.Conflict("lookup.already_active", "This entry is already active.");

    public static readonly Error LookupAlreadyInactive =
        Error.Conflict("lookup.already_inactive", "This entry is already inactive.");

    public static readonly Error LookupNotFound =
        Error.NotFound("platform.lookup_not_found", "That entry was not found.");
}
