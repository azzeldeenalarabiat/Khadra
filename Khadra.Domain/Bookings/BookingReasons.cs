using Khadra.Domain.Common;

namespace Khadra.Domain.Bookings;

/// <summary>
/// Why a customer walked away from a booking.
/// </summary>
/// <remarks>
/// A closed set rather than free text, for the same reason the dealer's rejection reasons are one:
/// a required free-text box produces "asdf", and neither the gallery nor the owner can count what a
/// customer typed. The details a customer adds are kept beside the code, never instead of it.
///
/// It is a smart enum because the platform stores the NAME and both parties read a label -- and the
/// label has to exist in Arabic as well as English. Composing the English sentence into the stored
/// reason, which is how the dealer's rejection reason was persisted until 2026-09-08, means an
/// Arabic-speaking customer reads English on their own booking and nothing downstream can fix it.
/// </remarks>
public sealed class BookingCancellationReason : Enumeration
{
    public static readonly BookingCancellationReason PlansChanged = new(1, "PlansChanged");
    public static readonly BookingCancellationReason FoundBetterPrice = new(2, "FoundBetterPrice");
    public static readonly BookingCancellationReason TravelCancelled = new(3, "TravelCancelled");
    public static readonly BookingCancellationReason BookedByMistake = new(4, "BookedByMistake");
    public static readonly BookingCancellationReason DealerUnresponsive = new(5, "DealerUnresponsive");
    public static readonly BookingCancellationReason Other = new(6, "Other");

    private BookingCancellationReason(int id, string name) : base(id, name)
    {
    }

    public static bool IsKnown(string? name) =>
        name is not null &&
        GetAll<BookingCancellationReason>().Any(reason => string.Equals(reason.Name, name, StringComparison.Ordinal));
}

/// <summary>
/// Why a gallery declined a request (spec 4.2).
/// </summary>
/// <remarks>
/// These codes were a dictionary of English sentences in the application layer, composed into the
/// stored reason as prose. They are a smart enum here so the code survives on the record and the
/// sentence is chosen by whoever is reading it, in their own language.
/// </remarks>
public sealed class BookingRejectionReason : Enumeration
{
    public static readonly BookingRejectionReason VehicleUnavailable = new(1, "VehicleUnavailable");
    public static readonly BookingRejectionReason DatesConflict = new(2, "DatesConflict");
    public static readonly BookingRejectionReason OutsideDeliveryRadius = new(3, "OutsideDeliveryRadius");
    public static readonly BookingRejectionReason CustomerVerificationIncomplete = new(4, "CustomerVerificationIncomplete");
    public static readonly BookingRejectionReason Other = new(5, "Other");

    private BookingRejectionReason(int id, string name) : base(id, name)
    {
    }

    public static bool IsKnown(string? name) =>
        name is not null &&
        GetAll<BookingRejectionReason>().Any(reason => string.Equals(reason.Name, name, StringComparison.Ordinal));
}
