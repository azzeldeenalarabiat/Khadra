using Khadra.Application.Common;
using Khadra.Application.Common.Dtos;
using Khadra.Domain.Common;

namespace Khadra.Application.Fleet.ReadModels;

/// <summary>
/// What a customer searching for a car can see.
/// </summary>
/// <remarks>
/// The primary row is a vehicle, so the port lives in Fleet — but a single result needs three
/// contexts to answer: Fleet owns the car, Dealers owns the gallery behind it (and whether that
/// gallery may trade at all), and Bookings decides whether it is free. They are correlated by id in
/// ONE query, which is the pattern `BookingReader` already established for the console.
///
/// One difference from `BookingReader` worth stating, because copying its shape without noticing
/// would be a real leak: its dealer correlation is deliberately LEFT, falling back to "Dealer no
/// longer on the platform", because a booking is a financial record that outlives the gallery. A
/// catalogue is the opposite. A car whose gallery is missing, unapproved, suspended or deleted must
/// simply not appear, so the correlation here is INNER.
///
/// Nothing in these records comes from `VehicleDto` or `DealerProfileDto`. Those carry the plate
/// number, the stored status, the commercial registration and the suspension reason — fields a
/// dealer or an administrator may see and an anonymous caller may not. Sharing the type would mean
/// the next field added for a console screen leaks through a public endpoint, silently.
/// </remarks>
public interface ICatalogueReader
{
    /// <summary>One page of cars a customer could book, newest listing first.</summary>
    Task<PagedResult<CatalogueListing>> SearchAsync(
        CatalogueFilter filter,
        PageRequest page,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// One car in full, or null when it is not something this caller may see.
    /// </summary>
    /// <remarks>
    /// Null covers every reason at once — no such car, a draft, a hidden one, one whose gallery is
    /// suspended or was never approved, one soft-deleted — and the endpoint answers 404 to all of
    /// them. Distinguishing them would let anyone enumerate a competitor's unpublished inventory
    /// through an endpoint that needs no sign-in.
    /// </remarks>
    /// <param name="language">
    /// The reader's language, used ONLY to resolve the office's own writing. Nothing about which cars
    /// are visible depends on it.
    /// </param>
    Task<CatalogueVehicle?> GetAsync(
        Id vehicleId,
        AvailabilityWindow? window,
        Language language,
        CancellationToken cancellationToken = default);

    /// <summary>A gallery's own page, or null if it is not one a customer may see.</summary>
    Task<PublicGalleryPage?> GetGalleryAsync(Id dealerId, Language language, CancellationToken cancellationToken = default);

    /// <summary>
    /// The listings for a named set of cars, through the SAME visibility predicate as the search.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For a screen that already holds ids and needs the cars behind them — today, a customer's
    /// shortlist. It exists so that screen does not get its own copy of the listing projection and
    /// its own idea of which cars a customer may see: two predicates drift, and the way they drift is
    /// one of them showing a suspended gallery's car to somebody who saved it before the suspension.
    /// </para>
    /// <para>
    /// An id that is not visible is simply ABSENT from the result — never an error, and never a row
    /// saying why. The caller is expected to notice the gap and render it as "no longer listed": the
    /// reason is exactly what this endpoint's silence is protecting, since a draft, a hidden car, a
    /// suspended gallery's and an unknown id must stay indistinguishable.
    /// </para>
    /// <para>
    /// No availability window: a set of ids carries no dates, and `IsAvailable` has no meaning
    /// without a period to ask about.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<CatalogueListing>> ListByIdsAsync(
        IReadOnlyCollection<Id> vehicleIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// What the bookable catalogue actually holds, for the controls a customer narrows it with.
    /// </summary>
    /// <remarks>
    /// Through the same visibility predicate as the search, so a draft, a hidden car or a suspended
    /// gallery's car never adds a choice. And deliberately NOT narrowed by any filter: these build the
    /// controls themselves, and a control that vanished whenever another one was set would make a
    /// choice impossible to take back.
    /// </remarks>
    Task<CatalogueFacets> FacetsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The rental offices a customer may be shown, most cars listed first.
    /// </summary>
    /// <remarks>
    /// "May be shown" is the same test the gallery's own page applies (approved, not suspended), and
    /// the car count is <c>Bookable()</c> — the search's own predicate — so the number on an office's
    /// card is exactly the number of cars its page lists. Two predicates would drift, and the drift
    /// would be an office advertising cars nobody can open.
    /// </remarks>
    Task<PagedResult<PublicGalleryCard>> ListGalleriesAsync(
        Id? cityId,
        PageRequest page,
        CancellationToken cancellationToken = default);
}

/// <summary>The values the bookable catalogue contains.</summary>
/// <remarks>
/// Seat counts ascending, and car type ids without names: the names are the lookup's, in both
/// languages. An app offers a type only when it appears in both — so a type with no bookable car
/// offers no chip, and neither does one an administrator has retired.
///
/// Makes, fuel types and years were added for the customer website's filters (2026-09-23). Added
/// fields, not changed ones: an installed app reads the two it knew and ignores the rest. Makes are as
/// the offices typed them, one entry per spelling-insensitive make; fuel types are the API names the
/// `/app-config` vocabulary labels; years are newest first.
/// </remarks>
public sealed record CatalogueFacets(
    IReadOnlyList<int> Seats,
    IReadOnlyList<Guid> CarTypeIds,
    IReadOnlyList<string> Makes,
    IReadOnlyList<string> FuelTypes,
    IReadOnlyList<int> Years);

/// <summary>
/// How a search is ordered. Newest listing first unless the customer chose otherwise.
/// </summary>
/// <remarks>
/// Every order ends in the same tie-break (newest listing, then id), because a non-total order lets a
/// page boundary drop a car or show it twice. Price orders compare the daily rate as stored; the
/// platform prices in one currency, so there is no conversion to get wrong.
/// </remarks>
public sealed class CatalogueSort : Enumeration
{
    public static readonly CatalogueSort Newest = new(1, "Newest");
    public static readonly CatalogueSort PriceLowToHigh = new(2, "PriceLowToHigh");
    public static readonly CatalogueSort PriceHighToLow = new(3, "PriceHighToLow");
    public static readonly CatalogueSort YearNewest = new(4, "YearNewest");

    private CatalogueSort(int id, string name) : base(id, name)
    {
    }
}

/// <summary>A rental office as a directory card: who, where, whether it delivers, how many cars.</summary>
/// <remarks>
/// Carries none of the office's own writing — that is its page's, and some of it may be hidden.
/// </remarks>
public sealed record PublicGalleryCard(
    Guid DealerId,
    string BusinessName,
    Guid? CityId,
    string? LogoUrl,
    string? CoverUrl,
    GalleryDelivery Delivery,
    decimal? AverageRating,
    int ReviewCount,
    int ListedVehicleCount);

/// <summary>
/// How a customer narrowed the search.
/// </summary>
/// <remarks>
/// Every field is optional except the availability inputs, because a customer who has named no
/// dates is browsing rather than booking and every listed car is a candidate.
/// </remarks>
public sealed record CatalogueFilter(
    Id? CityId = null,
    Id? CarTypeId = null,
    Id? DealerId = null,
    decimal? MinDailyRate = null,
    decimal? MaxDailyRate = null,
    string? Transmission = null,
    int? MinSeats = null,
    bool DeliveryOnly = false,
    // Matched against make and model, case-insensitively. Not a full-text search: the columns are
    // plain strings on an owned type, so `Contains` translates on both Postgres and the SQLite the
    // persistence tests run against.
    string? Text = null,
    AvailabilityWindow? Window = null,
    // By API name, like Transmission; an unknown name matches nothing rather than everything.
    string? FuelType = null,
    // The whole make, case-insensitively — a choice from the facets, not a search term.
    string? Make = null,
    int? MinYear = null,
    int? MaxYear = null,
    CatalogueSort? Sort = null);

/// <summary>
/// The dates a customer asked about, and everything needed to judge them.
/// </summary>
/// <remarks>
/// `Now` and `TurnaroundBuffer` travel with the period rather than being read inside the reader:
/// business numbers come from `IBusinessRulesProvider` and the clock from `IClock`, and neither
/// belongs to infrastructure. The buffer is TODAY's figure, applied to the candidate only — each
/// stored booking already carries the gap it froze.
/// </remarks>
public sealed record AvailabilityWindow(DateRange Period, DateTimeOffset Now, TimeSpan TurnaroundBuffer)
{
    /// <summary>The instant this candidate rental would start claiming the car.</summary>
    public DateTimeOffset HoldStart => Period.Start.Subtract(TurnaroundBuffer);
}

/// <summary>One car as a search result row.</summary>
/// <remarks>
/// Deliberately smaller than the detail response. A phone on a Jordanian mobile network pulls
/// twenty of these at a time, and the deposit, the mileage policy and the delivery fee are all
/// answers to "what would this cost me", which is the quote endpoint's job, not a list's.
/// </remarks>
public sealed record CatalogueListing(
    Guid VehicleId,
    string Make,
    string Model,
    int Year,
    // Correlated from car_types rather than joined, so a category an administrator has since retired
    // still has a name on a car that was listed under it (pre-launch checklist item 29).
    CatalogueCarType? CarType,
    string Transmission,
    string FuelType,
    int Seats,
    // Null is a real answer: Publish demands a photo, but RemoveImage never re-checks status, so an
    // Active car can have none. The app renders a placeholder, never a broken image.
    string? CoverImageUrl,
    MoneyDto DailyRate,
    // Both halves must be true: the gallery offers delivery AND this car is eligible for it.
    bool IsDeliveryAvailable,
    CatalogueGalleryLabel Gallery);

/// <summary>The gallery behind a search row: enough to recognise it, nothing more.</summary>
/// <remarks>
/// The rating is the GALLERY's, and it sits here rather than on the car deliberately. The platform
/// rates rental offices, not vehicles (spec 4.1, and `Review` feeds the dealer's rating) -- so a
/// per-car score is not a feature that is merely unbuilt, it is a thing this domain does not model.
/// A renter comparing two Corollas is really choosing between two offices, which is what this says.
///
/// Null and zero until Reviews has a table (pre-launch checklist item 3). The fields exist now so the
/// card has a rendering path that lights up the day the data does, without an app change.
/// </remarks>
public sealed record CatalogueGalleryLabel(
    Guid DealerId,
    string BusinessName,
    Guid? CityId,
    string? LogoUrl,
    decimal? AverageRating,
    int ReviewCount);

public sealed record CatalogueCarType(Guid CarTypeId, string NameEn, string NameAr);

/// <summary>One car in full, as its own screen shows it.</summary>
public sealed record CatalogueVehicle(
    Guid VehicleId,
    string Make,
    string Model,
    int Year,
    string? Color,
    ResolvedTextDto? Description,
    CatalogueCarType? CarType,
    string Transmission,
    string FuelType,
    int Seats,
    MoneyDto DailyRate,
    MoneyDto SecurityDeposit,
    MileagePolicyView Mileage,
    string FuelPolicy,
    bool IsDeliveryEligible,
    IReadOnlyList<string> ImageUrls,
    PublicGallery Gallery,
    // Null when the customer named no dates: "is it free" has no answer without a period to ask
    // about, and false would be a lie.
    bool? IsAvailable);

/// <summary>What a customer is told about mileage before booking.</summary>
public sealed record MileagePolicyView(bool IsUnlimited, int? DailyLimitKm, MoneyDto? ExcessFeePerKm);

/// <summary>
/// A gallery's public face, as it appears BESIDE A CAR.
/// </summary>
/// <remarks>
/// What is NOT here matters as much as what is: no commercial registration number, no review note,
/// no suspension reason, no verification status, no owner. A suspended or unapproved gallery is not
/// returned at all, so a status field would only ever read "Approved" and invite someone to add the
/// others beside it.
///
/// And none of the office's own writing. This record travels inside every car, and the office's
/// sections are its page's to show — including the ones it has HIDDEN, which is exactly the kind of
/// thing that leaks when one record serves two screens. <see cref="PublicGalleryPage"/> is the page.
/// </remarks>
public sealed record PublicGallery(
    Guid DealerId,
    string BusinessName,
    Guid? CityId,
    double Latitude,
    double Longitude,
    string? LogoUrl,
    string? CoverUrl,
    IReadOnlyList<GalleryDaySchedule> OperatingHours,
    GalleryDelivery Delivery,
    // Reviews has a domain model and no persistence (pre-launch checklist item 3). Null and 0 are
    // the honest values; omitting the fields would make a client invent its own placeholder.
    decimal? AverageRating,
    int ReviewCount);

/// <summary>
/// A gallery's OWN page: everything the embed carries, plus where it is in words and what it writes
/// for customers.
/// </summary>
/// <remarks>
/// A separate record from <see cref="PublicGallery"/> rather than a superset flag, so a section an
/// office hid cannot reach a customer through a car's embedded gallery by accident. What is mandatory
/// here — the hours a pickup is held to, whether delivery is offered and at what price, the address,
/// the pin, the rating — is not part of <see cref="Sections"/> and cannot be hidden.
/// </remarks>
public sealed record PublicGalleryPage(
    Guid DealerId,
    string BusinessName,
    Guid? CityId,
    GalleryAddress? Address,
    double Latitude,
    double Longitude,
    string? LogoUrl,
    string? CoverUrl,
    IReadOnlyList<GalleryDaySchedule> OperatingHours,
    GalleryDelivery Delivery,
    decimal? AverageRating,
    int ReviewCount,
    GallerySections Sections);

/// <summary>Where the office is, in words. Null until an owner records one.</summary>
public sealed record GalleryAddress(string Area, string? Street);

/// <summary>
/// What the office wrote for its customers, as a customer sees it.
/// </summary>
/// <remarks>
/// Null means "nothing to show" and deliberately does not say why: hidden, never written, and — for
/// delivery notes — an office that does not deliver all read the same. There is no flag saying a
/// section exists but is hidden, because that flag would be the answer the silence is protecting.
/// </remarks>
public sealed record GallerySections(
    ResolvedTextDto? About,
    ResolvedTextDto? RentalConditions,
    ResolvedTextDto? Insurance,
    ResolvedTextDto? PickupInstructions,
    ResolvedTextDto? DeliveryNotes,
    ResolvedTextDto? CustomerNotes);

public sealed record GalleryDaySchedule(string Day, bool IsClosed, TimeOnly? Opens, TimeOnly? Closes);

/// <summary>
/// What this gallery charges to bring the car to you, and how far it will come.
/// </summary>
/// <remarks>
/// The fee is the gallery's own figure (owner, 2026-09-06) and is null exactly when delivery is off.
/// There is no platform-wide delivery fee to fall back on, so a client must not invent one.
/// </remarks>
public sealed record GalleryDelivery(bool IsEnabled, decimal RadiusKm, MoneyDto? Fee);
