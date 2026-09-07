using CSharpFunctionalExtensions;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.Fleet;
using MediatR;

namespace Khadra.Application.PlatformSettings.AppConfig;

/// <summary>
/// What a client needs to know about the platform before it can ask it anything sensible.
/// </summary>
/// <remarks>
/// Every value here is one the server already owns and a client would otherwise have to hard-code —
/// which for a phone means a new release in every shop each time the owner changes a number.
///
/// The time zone is the sharpest example. Rentals are billed in Amman calendar days, and a date
/// picker has to run in that zone or it prices a different number of days. The quote response
/// carries the zone, but the picker runs BEFORE the first quote, so without this the app either
/// guesses or ships "+03:00" in a widget.
///
/// The vocabularies are here for the standing rule rather than for convenience: a filter chip is
/// something a customer reads, so its text cannot be a literal in the app.
/// </remarks>
public sealed record GetAppConfigQuery : IQuery<Result<AppConfigDto, Error>>;

public sealed record AppConfigDto(
    /// The IANA zone every calendar answer on this platform is expressed in.
    string TimeZone,
    CurrencyDto Currency,
    /// Null means the owner has set no age limit and nobody is refused on age -- a real shipping
    /// state, not a missing value.
    int? MinimumRenterAge,
    /// How far ahead a rental may be booked. The date picker needs a bound, and this is the owner's
    /// figure rather than one baked into a phone binary that only a release could change.
    int MaxAdvanceBookingDays,
    /// The soonest a rental may start, from now. The date picker needs both bounds, and a phone that
    /// guessed either would offer a slot the server refuses.
    int MinimumBookingLeadTimeMinutes,
    /// The longest a single rental may run, in calendar days. The picker needs it for the same
    /// reason it needs the other two bounds: so it cannot offer a span the server will refuse.
    int MaxRentalDays,
    /// How long after a dealer approves the customer has to pay the deposit. The app tells them,
    /// so it must come from the platform rather than from a sentence typed into a screen.
    int PaymentWindowHours,
    DocumentLimitsDto Documents,
    VocabulariesDto Vocabularies);

/// <summary>
/// The platform's currency, and how many decimals it is written with.
/// </summary>
/// <remarks>
/// Sent rather than assumed because the dinar has THREE, and every developer's instinct is two. A
/// client that guesses renders 12.75 where the contract says 12.750.
/// </remarks>
public sealed record CurrencyDto(string Code, int MinorUnits);

public sealed record DocumentLimitsDto(long MaximumSizeBytes, IReadOnlyList<string> AllowedContentTypes);

/// <summary>
/// The closed sets a customer chooses from, in both languages.
/// </summary>
/// <remarks>
/// These are smart enums in the domain, so their MEMBERS are part of the API contract and a client
/// may compare against the name. The label is what it shows. Sending both keeps the comparison
/// stable while the words stay the platform's to change.
/// </remarks>
public sealed record VocabulariesDto(
    IReadOnlyList<VocabularyEntryDto> Transmissions,
    IReadOnlyList<VocabularyEntryDto> FuelTypes,
    IReadOnlyList<VocabularyEntryDto> PickupMethods,
    /// <summary>
    /// Why a customer may say they are cancelling. The chips on the cancel sheet come from here, and
    /// the code is what is stored on the booking -- so the sentence is chosen when it is READ, in the
    /// reader's own language, rather than frozen in one language on a permanent record.
    /// </summary>
    IReadOnlyList<VocabularyEntryDto> CancellationReasons,
    /// <summary>
    /// Why a gallery declined. A customer needs it to render the reason on their own booking; the
    /// dealer console needs it to offer the choice. One list serves both.
    /// </summary>
    IReadOnlyList<VocabularyEntryDto> RejectionReasons);

public sealed record VocabularyEntryDto(string Name, string LabelEn, string LabelAr);

public sealed class GetAppConfigHandler(
    IReportingCalendar calendar,
    IBusinessRulesProvider businessRules,
    IDocumentPolicySettings documents)
    : IRequestHandler<GetAppConfigQuery, Result<AppConfigDto, Error>>
{
    public async Task<Result<AppConfigDto, Error>> Handle(
        GetAppConfigQuery request,
        CancellationToken cancellationToken)
    {
        var rules = await businessRules.GetAsync(cancellationToken);

        return new AppConfigDto(
            calendar.TimeZoneId,
            new CurrencyDto(Money.JordanianDinar, Money.MinorUnits),
            rules.MinimumRenterAge,
            rules.MaxAdvanceBookingDays,
            rules.MinimumBookingLeadTimeMinutes,
            rules.MaxRentalDays,
            rules.PaymentWindowHours,
            new DocumentLimitsDto(documents.MaximumSizeBytes, [.. documents.AllowedContentTypes]),
            new VocabulariesDto(
                [.. Enumeration.GetAll<TransmissionType>().Select(Vocabulary.Describe)],
                [.. Enumeration.GetAll<FuelType>().Select(Vocabulary.Describe)],
                [.. Enumeration.GetAll<PickupMethod>().Select(Vocabulary.Describe)],
                [.. Enumeration.GetAll<BookingCancellationReason>().Select(Vocabulary.Describe)],
                [.. Enumeration.GetAll<BookingRejectionReason>().Select(Vocabulary.Describe)]));
    }
}

/// <summary>
/// The Arabic for the platform's closed sets.
/// </summary>
/// <remarks>
/// It lives here rather than in the app because these words describe the PLATFORM's concepts, and
/// because a second copy in a phone binary can only drift from this one. When the owner wants a
/// lookup table an administrator can edit, this is the seam it replaces.
///
/// A member with no translation falls back to its own name rather than throwing: a new fuel type
/// added tomorrow must not take the whole config endpoint down with it.
/// </remarks>
internal static class Vocabulary
{
    private static readonly Dictionary<string, (string En, string Ar)> Labels = new(StringComparer.Ordinal)
    {
        ["Automatic"] = ("Automatic", "أوتوماتيك"),
        ["Manual"] = ("Manual", "عادي"),
        ["Petrol"] = ("Petrol", "بنزين"),
        ["Diesel"] = ("Diesel", "ديزل"),
        ["Hybrid"] = ("Hybrid", "هجين"),
        ["Electric"] = ("Electric", "كهربائي"),
        ["SelfPickup"] = ("Collect it yourself", "الاستلام من المكتب"),
        ["Delivery"] = ("Delivered to you", "التوصيل إليك"),

        // Why a customer cancelled. Written as the customer's own voice, because that is who picks
        // one and who reads it back on their booking afterwards.
        ["PlansChanged"] = ("My plans changed", "تغيّرت خططي"),
        ["FoundBetterPrice"] = ("I found a better price", "وجدت سعراً أفضل"),
        ["TravelCancelled"] = ("My trip was cancelled", "أُلغيت رحلتي"),
        ["BookedByMistake"] = ("I booked this by mistake", "حجزت عن طريق الخطأ"),
        ["DealerUnresponsive"] = ("The rental office did not respond", "لم يستجب مكتب التأجير"),
        // Qualified by type: "Other" means different things to the two parties, and both sets have a
        // member by that name. An unqualified key would have silently given the gallery's refusal the
        // customer's wording.
        ["BookingCancellationReason:Other"] = ("Another reason", "سبب آخر"),

        // Why a gallery declined. Written to be read BY the customer, since that is where it lands:
        // "we" is the gallery speaking. Until 2026-09-08 the English of these was composed into the
        // stored reason, so an Arabic-speaking customer read English on their own booking.
        ["VehicleUnavailable"] = ("The vehicle is no longer available", "المركبة لم تعد متاحة"),
        ["DatesConflict"] = ("The dates conflict with another booking", "التواريخ تتعارض مع حجز آخر"),
        ["OutsideDeliveryRadius"] = ("The delivery location is outside our delivery area", "موقع التوصيل خارج نطاق خدمتنا"),
        ["CustomerVerificationIncomplete"] = ("Your documents could not be verified", "تعذّر التحقق من مستنداتك"),
        ["BookingRejectionReason:Other"] = ("Declined by the rental office", "رُفض من قِبل مكتب التأجير"),
    };

    /// <summary>
    /// The label for one member, looked up by type-qualified name first and by bare name after.
    /// </summary>
    /// <remarks>
    /// Two sets both have a member called "Other" and they do not mean the same thing -- a customer's
    /// "another reason" against a gallery's "declined by the rental office". Bare names alone would
    /// have given one of them the other's words, silently and in both languages. Everything without a
    /// collision stays keyed by its plain name so the table reads as a vocabulary rather than a
    /// namespace.
    /// </remarks>
    public static VocabularyEntryDto Describe(Enumeration member)
    {
        ArgumentNullException.ThrowIfNull(member);

        var qualified = $"{member.GetType().Name}:{member.Name}";
        if (Labels.TryGetValue(qualified, out var scoped))
            return new VocabularyEntryDto(member.Name, scoped.En, scoped.Ar);

        // A member added tomorrow with no translation falls back to its own name rather than taking
        // the whole config endpoint -- and with it every client's startup -- down with it.
        return Labels.TryGetValue(member.Name, out var label)
            ? new VocabularyEntryDto(member.Name, label.En, label.Ar)
            : new VocabularyEntryDto(member.Name, member.Name, member.Name);
    }
}
