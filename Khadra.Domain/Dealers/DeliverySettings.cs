using CSharpFunctionalExtensions;
using Khadra.Domain.Common;
using ValueObject = Khadra.Domain.Common.ValueObject;

namespace Khadra.Domain.Dealers;

/// <summary>
/// Spec 4.4: whether a dealership delivers, how far it will drive, and what it charges to do it.
///
/// The fee used to be platform-wide — one configured figure every gallery had to charge — and the
/// owner has moved it here, to the business that actually performs the delivery. The domain was
/// already shaped for that: <c>BookingPricing</c> leaves the fee out of the deposit base, so it
/// never touches the platform's commission, and puts it in <c>BalanceDue</c>, which the driver
/// collects in cash on arrival. The money was always the gallery's; only the authority to set it
/// sat elsewhere.
///
/// A fee exists only while delivery is on. Switching delivery off does not remember the old price:
/// a gallery that comes back to delivery is quoting again, and a stale figure silently restored is
/// the kind of number nobody remembers choosing.
/// </summary>
public sealed class DeliverySettings : ValueObject
{
    public const decimal MaxRadiusKm = 200m;

    /// <summary>
    /// A ceiling on the fee, in the platform's currency.
    ///
    /// A typo guard, not a business rule: it exists so a gallery that means 15 and types 150000
    /// is stopped, in the same spirit as <see cref="MaxRadiusKm"/>. What a delivery is worth is the
    /// gallery's decision, and nothing here narrows it beyond the absurd.
    /// </summary>
    public const decimal MaxFee = 1000m;

    // A fresh instance per call: this is assigned to every dealer that has not enabled delivery, and
    // a shared instance would leave many dealers owning the same tracked value object.
    public static DeliverySettings Disabled => new(false, 0m, null);

    public bool IsEnabled { get; }
    public decimal RadiusKm { get; }

    /// <summary>What this gallery charges for a delivery. Null exactly when delivery is off.</summary>
    public Money? Fee { get; }

#pragma warning disable CS8618 // EF materialises this value object by writing its backing fields.
    /// <summary>
    /// For EF only. An owned reference — the fee — cannot be bound to a constructor parameter, so
    /// the moment this value object gained one, EF could no longer materialise a dealership at all.
    /// The same pattern <see cref="Money"/> uses, and for the same reason: reading a stored value
    /// must not re-run the factory guards, or a rule tightened later would make old rows unreadable.
    /// </summary>
    private DeliverySettings()
    {
    }
#pragma warning restore CS8618

    private DeliverySettings(bool isEnabled, decimal radiusKm, Money? fee)
    {
        IsEnabled = isEnabled;
        RadiusKm = radiusKm;
        Fee = fee;
    }

    /// <param name="fee">
    /// May be zero: a gallery that delivers free of charge is making a real offer, and refusing it
    /// would be the platform pricing on their behalf again.
    /// </param>
    public static Result<DeliverySettings, Error> Enabled(decimal radiusKm, Money fee)
    {
        ArgumentNullException.ThrowIfNull(fee);

        if (radiusKm <= 0m || radiusKm > MaxRadiusKm)
            return DealerErrors.InvalidDeliveryRadius;
        // Money already refuses a negative amount, so only the ceiling is left to check.
        if (fee.Amount > MaxFee)
            return DealerErrors.InvalidDeliveryFee;

        return new DeliverySettings(true, decimal.Round(radiusKm, 2, MidpointRounding.ToEven), fee);
    }

    public bool Covers(GeoPoint dealerLocation, GeoPoint destination)
    {
        ArgumentNullException.ThrowIfNull(dealerLocation);
        ArgumentNullException.ThrowIfNull(destination);

        return IsEnabled && (decimal)dealerLocation.DistanceKmTo(destination) <= RadiusKm;
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return IsEnabled;
        yield return RadiusKm;
        yield return Fee;
    }
}
