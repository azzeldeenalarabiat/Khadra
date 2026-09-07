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
    IReadOnlyList<VocabularyEntryDto> PickupMethods);

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
            new DocumentLimitsDto(documents.MaximumSizeBytes, [.. documents.AllowedContentTypes]),
            new VocabulariesDto(
                [.. Enumeration.GetAll<TransmissionType>().Select(Vocabulary.Describe)],
                [.. Enumeration.GetAll<FuelType>().Select(Vocabulary.Describe)],
                [.. Enumeration.GetAll<PickupMethod>().Select(Vocabulary.Describe)]));
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
    };

    public static VocabularyEntryDto Describe(Enumeration member)
    {
        ArgumentNullException.ThrowIfNull(member);
        return Labels.TryGetValue(member.Name, out var label)
            ? new VocabularyEntryDto(member.Name, label.En, label.Ar)
            : new VocabularyEntryDto(member.Name, member.Name, member.Name);
    }
}
