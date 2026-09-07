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
    Task<CatalogueVehicle?> GetAsync(
        Id vehicleId,
        AvailabilityWindow? window,
        CancellationToken cancellationToken = default);

    /// <summary>A gallery's public page, or null if it is not one a customer may see.</summary>
    Task<PublicGallery?> GetGalleryAsync(Id dealerId, CancellationToken cancellationToken = default);
}

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
    AvailabilityWindow? Window = null);

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
public sealed record CatalogueGalleryLabel(
    Guid DealerId,
    string BusinessName,
    Guid? CityId,
    string? LogoUrl);

public sealed record CatalogueCarType(Guid CarTypeId, string NameEn, string NameAr);

/// <summary>One car in full, as its own screen shows it.</summary>
public sealed record CatalogueVehicle(
    Guid VehicleId,
    string Make,
    string Model,
    int Year,
    string? Color,
    string? Description,
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
/// A gallery's public face.
/// </summary>
/// <remarks>
/// What is NOT here matters as much as what is: no commercial registration number, no review note,
/// no suspension reason, no verification status, no owner. A suspended or unapproved gallery is not
/// returned at all, so a status field would only ever read "Approved" and invite someone to add the
/// others beside it.
/// </remarks>
public sealed record PublicGallery(
    Guid DealerId,
    string BusinessName,
    string? Description,
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

public sealed record GalleryDaySchedule(string Day, bool IsClosed, TimeOnly? Opens, TimeOnly? Closes);

/// <summary>
/// What this gallery charges to bring the car to you, and how far it will come.
/// </summary>
/// <remarks>
/// The fee is the gallery's own figure (owner, 2026-09-06) and is null exactly when delivery is off.
/// There is no platform-wide delivery fee to fall back on, so a client must not invent one.
/// </remarks>
public sealed record GalleryDelivery(bool IsEnabled, decimal RadiusKm, MoneyDto? Fee);
