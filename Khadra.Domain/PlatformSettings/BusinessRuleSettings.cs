using CSharpFunctionalExtensions;
using Khadra.Domain.Common;

namespace Khadra.Domain.PlatformSettings;

// The single row holding every business number from spec section 2. It exists so the Admin can tune
// the platform after launch without a deploy, and so no percentage is ever a constant in code.
//
// Every consumer receives these values as explicit parameters; nothing else in the domain reads this
// aggregate directly, which keeps the Booking and Payments contexts independent of it.
public sealed class BusinessRuleSettings : AggregateRoot
{
    public Percentage CommissionPercent { get; private set; } = null!;
    public Percentage DepositPercent { get; private set; } = null!;
    public int NoShowTimeoutHours { get; private set; }
    public Money DeliveryFee { get; private set; } = null!;
    public Percentage DealerNonDeliveryPenaltyMinPercent { get; private set; } = null!;
    public Percentage DealerNonDeliveryPenaltyMaxPercent { get; private set; } = null!;
    public int FreeCancellationWindowMinutes { get; private set; }
    // Spec 2.3, still undecided: the payment provider keeps its fee even on a refunded deposit, so the
    // owner may want to pass a small fee on. Null means "absorb it", which is the current behaviour.
    public Money? QuickCancellationProcessingFee { get; private set; }
    public int AdminSlaHours { get; private set; }
    // Spec 2.2, still undecided by the owner. Null means the rule is not enforced yet.
    public int? MinimumRenterAge { get; private set; }
    public bool? RequireInternationalPermitForForeigners { get; private set; }
    // Bumped on every change so an old booking can be traced to the terms that applied to it.
    public int Version { get; private set; }
    public Id? UpdatedByAdminId { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private BusinessRuleSettings()
    {
    }

    private BusinessRuleSettings(Id id) : base(id)
    {
    }

    public static Result<BusinessRuleSettings, Error> Create(
        Percentage commissionPercent,
        Percentage depositPercent,
        int noShowTimeoutHours,
        Money deliveryFee,
        Percentage penaltyMinPercent,
        Percentage penaltyMaxPercent,
        int freeCancellationWindowMinutes,
        int adminSlaHours,
        DateTimeOffset now)
    {
        var settings = new BusinessRuleSettings(Id.New()) { Version = 1, UpdatedAt = now };
        var applied = settings.Apply(
            commissionPercent,
            depositPercent,
            noShowTimeoutHours,
            deliveryFee,
            penaltyMinPercent,
            penaltyMaxPercent,
            freeCancellationWindowMinutes,
            adminSlaHours);

        return applied.IsFailure ? applied.Error : settings;
    }

    public UnitResult<Error> Update(
        Percentage commissionPercent,
        Percentage depositPercent,
        int noShowTimeoutHours,
        Money deliveryFee,
        Percentage penaltyMinPercent,
        Percentage penaltyMaxPercent,
        int freeCancellationWindowMinutes,
        int adminSlaHours,
        Id adminUserId,
        DateTimeOffset now)
    {
        var applied = Apply(
            commissionPercent,
            depositPercent,
            noShowTimeoutHours,
            deliveryFee,
            penaltyMinPercent,
            penaltyMaxPercent,
            freeCancellationWindowMinutes,
            adminSlaHours);
        if (applied.IsFailure)
            return applied;

        Version++;
        UpdatedByAdminId = adminUserId;
        UpdatedAt = now;
        return UnitResult.Success<Error>();
    }

    public void SetQuickCancellationProcessingFee(Money? fee) => QuickCancellationProcessingFee = fee;

    public UnitResult<Error> SetMinimumRenterAge(int? minimumAge)
    {
        if (minimumAge is not null && minimumAge is < 18 or > 30)
            return UnitResult.Failure(PlatformSettingsErrors.InvalidMinimumAge);

        MinimumRenterAge = minimumAge;
        return UnitResult.Success<Error>();
    }

    public void SetInternationalPermitRequirement(bool? isRequired) =>
        RequireInternationalPermitForForeigners = isRequired;

    // The decisions the owner has not made yet (spec 2.2 and 2.3). Surfaced so the Admin console can
    // show them as outstanding rather than pretending a default is a decision.
    public IReadOnlyList<string> PendingOwnerDecisions()
    {
        var pending = new List<string>();
        if (DealerNonDeliveryPenaltyMinPercent != DealerNonDeliveryPenaltyMaxPercent)
            pending.Add("Dealer non-delivery penalty is still a range; confirm a single rate or a tier rule.");
        if (QuickCancellationProcessingFee is null)
            pending.Add("Quick-cancellation processing fee is unset; the platform currently absorbs the provider fee.");
        if (MinimumRenterAge is null)
            pending.Add("Minimum renter age is not set, so age is not enforced at registration.");
        if (RequireInternationalPermitForForeigners is null)
            pending.Add("International driving permit requirement for foreign renters is undecided.");
        return pending;
    }

    private UnitResult<Error> Apply(
        Percentage commissionPercent,
        Percentage depositPercent,
        int noShowTimeoutHours,
        Money deliveryFee,
        Percentage penaltyMinPercent,
        Percentage penaltyMaxPercent,
        int freeCancellationWindowMinutes,
        int adminSlaHours)
    {
        ArgumentNullException.ThrowIfNull(commissionPercent);
        ArgumentNullException.ThrowIfNull(depositPercent);
        ArgumentNullException.ThrowIfNull(deliveryFee);
        ArgumentNullException.ThrowIfNull(penaltyMinPercent);
        ArgumentNullException.ThrowIfNull(penaltyMaxPercent);

        // Spec 2.1: commission is taken out of the card deposit. If the deposit were smaller than the
        // commission the platform would have to chase the dealer for the difference on every booking,
        // which is exactly the leakage this design exists to prevent.
        if (commissionPercent.IsGreaterThan(depositPercent))
            return UnitResult.Failure(PlatformSettingsErrors.DepositMustCoverCommission);
        if (penaltyMinPercent.IsGreaterThan(penaltyMaxPercent))
            return UnitResult.Failure(PlatformSettingsErrors.PenaltyRangeInverted);
        if (noShowTimeoutHours is < 1 or > 720)
            return UnitResult.Failure(PlatformSettingsErrors.InvalidNoShowTimeout);
        if (freeCancellationWindowMinutes is < 0 or > 10080)
            return UnitResult.Failure(PlatformSettingsErrors.InvalidCancellationWindow);
        if (adminSlaHours is < 1 or > 720)
            return UnitResult.Failure(PlatformSettingsErrors.InvalidSla);

        CommissionPercent = commissionPercent;
        DepositPercent = depositPercent;
        NoShowTimeoutHours = noShowTimeoutHours;
        DeliveryFee = deliveryFee;
        DealerNonDeliveryPenaltyMinPercent = penaltyMinPercent;
        DealerNonDeliveryPenaltyMaxPercent = penaltyMaxPercent;
        FreeCancellationWindowMinutes = freeCancellationWindowMinutes;
        AdminSlaHours = adminSlaHours;
        return UnitResult.Success<Error>();
    }

    public TimeSpan NoShowTimeout => TimeSpan.FromHours(NoShowTimeoutHours);

    public TimeSpan FreeCancellationWindow => TimeSpan.FromMinutes(FreeCancellationWindowMinutes);

    public TimeSpan AdminSla => TimeSpan.FromHours(AdminSlaHours);
}
