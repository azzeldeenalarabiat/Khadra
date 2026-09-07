using CSharpFunctionalExtensions;
using Khadra.Application.Bookings;
using Khadra.Application.Bookings.Dtos;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.Fleet.ReadModels;
using Khadra.Domain.Bookings;
using Khadra.Domain.Bookings.Repositories;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers.Repositories;
using Khadra.Domain.Fleet.Repositories;
using MediatR;

namespace Khadra.Application.Fleet.BrowseCatalogue;

// What a customer can see before they have an account: the cars on the platform, one car in full,
// a gallery's page, and what a rental would cost for chosen dates.
//
// These are the only anonymous read endpoints on the platform. Everything they return is a public
// price or a public listing; nothing here names a customer, a plate, a registration number or a
// gallery that cannot currently trade.

/// <summary>Search the catalogue. Every filter is optional; dates are optional too.</summary>
public sealed record SearchCatalogueQuery(
    Guid? CityId,
    Guid? CarTypeId,
    Guid? DealerId,
    decimal? MinDailyRate,
    decimal? MaxDailyRate,
    string? Transmission,
    int? MinSeats,
    bool DeliveryOnly,
    string? Text,
    DateTimeOffset? PickupAt,
    DateTimeOffset? ReturnAt,
    PageRequest Page) : IQuery<Result<PagedResult<CatalogueListing>, Error>>;

/// <summary>One car's own page.</summary>
public sealed record GetCatalogueVehicleQuery(Id VehicleId, DateTimeOffset? PickupAt, DateTimeOffset? ReturnAt)
    : IQuery<Result<CatalogueVehicle, Error>>;

/// <summary>A gallery's public page.</summary>
public sealed record GetPublicGalleryQuery(Id DealerId) : IQuery<Result<PublicGallery, Error>>;

/// <summary>
/// What a named rental would cost, priced by the server.
/// </summary>
/// <remarks>
/// Instants, not dates. Availability is an instant question — a car comes back at 10:00, not "on
/// Thursday" — while the PRICE is a calendar-day question. The server resolves both and returns the
/// day count it used, so the app never counts days itself.
/// </remarks>
public sealed record QuoteRentalQuery(
    Id VehicleId,
    DateTimeOffset PickupAt,
    DateTimeOffset ReturnAt,
    string PickupMethod,
    double? Latitude,
    double? Longitude) : IQuery<Result<RentalQuote, Error>>;

/// <summary>A priced offer, before anything is booked.</summary>
/// <remarks>
/// Carries the frozen-shape pricing a booking would be created with, so the confirmation screen and
/// the booking detail screen render identical numbers from an identical shape. `TimeZone` travels
/// with it because a traveller quoting from London must be shown Amman times, not their own.
/// </remarks>
public sealed record RentalQuote(
    Guid VehicleId,
    DateTimeOffset PickupAt,
    DateTimeOffset ReturnAt,
    string TimeZone,
    string PickupMethod,
    BookingPricingDto Pricing,
    QuoteTerms Terms,
    bool IsAvailable);

/// <summary>The part of the frozen rules a customer needs before agreeing to them.</summary>
/// <remarks>
/// A deliberate subset of <see cref="BookingTermsDto"/>: the commission split and the dealer penalty
/// range are between the platform and the gallery, and mean nothing to a renter.
/// </remarks>
public sealed record QuoteTerms(
    decimal DepositPercent,
    double FreeCancellationWindowHours,
    double PaymentWindowMinutes,
    decimal CustomerCancellationPenaltyPercent,
    double NoShowTimeoutHours);

public sealed class SearchCatalogueHandler(
    ICatalogueReader catalogue,
    IBusinessRulesProvider businessRules,
    IClock clock)
    : IRequestHandler<SearchCatalogueQuery, Result<PagedResult<CatalogueListing>, Error>>
{
    public async Task<Result<PagedResult<CatalogueListing>, Error>> Handle(
        SearchCatalogueQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var window = await BuildWindowAsync(request.PickupAt, request.ReturnAt, businessRules, clock, cancellationToken);
        if (window.IsFailure)
            return window.Error;

        var filter = new CatalogueFilter(
            request.CityId is { } city ? Id.From(city) : null,
            request.CarTypeId is { } carType ? Id.From(carType) : null,
            request.DealerId is { } dealer ? Id.From(dealer) : null,
            request.MinDailyRate,
            request.MaxDailyRate,
            request.Transmission,
            request.MinSeats,
            request.DeliveryOnly,
            request.Text,
            window.Value);

        return await catalogue.SearchAsync(filter, request.Page, cancellationToken);
    }

    /// <summary>
    /// Turns an optional pair of instants into an availability window, or into nothing.
    /// </summary>
    /// <remarks>
    /// Shared by search and the single listing because both accept the same optional dates and must
    /// judge them the same way. Half a period is a client bug, not a browse: silently ignoring one
    /// end would show a customer cars that are not free on the dates they typed.
    /// </remarks>
    internal static async Task<Result<AvailabilityWindow?, Error>> BuildWindowAsync(
        DateTimeOffset? pickupAt,
        DateTimeOffset? returnAt,
        IBusinessRulesProvider businessRules,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (pickupAt is null && returnAt is null)
            return (AvailabilityWindow?)null;

        if (pickupAt is null || returnAt is null)
        {
            return Error.Validation(
                "catalogue.incomplete_period",
                "Give both a pickup and a return time, or neither.");
        }

        var period = DateRange.Create(pickupAt.Value, returnAt.Value);
        if (period.IsFailure)
            return period.Error;

        var rules = await businessRules.GetAsync(cancellationToken);
        return new AvailabilityWindow(
            period.Value,
            clock.UtcNow,
            TimeSpan.FromMinutes(rules.TurnaroundMinutes));
    }
}

public sealed class GetCatalogueVehicleHandler(
    ICatalogueReader catalogue,
    IBusinessRulesProvider businessRules,
    IClock clock)
    : IRequestHandler<GetCatalogueVehicleQuery, Result<CatalogueVehicle, Error>>
{
    public async Task<Result<CatalogueVehicle, Error>> Handle(
        GetCatalogueVehicleQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var window = await SearchCatalogueHandler.BuildWindowAsync(
            request.PickupAt, request.ReturnAt, businessRules, clock, cancellationToken);
        if (window.IsFailure)
            return window.Error;

        var vehicle = await catalogue.GetAsync(request.VehicleId, window.Value, cancellationToken);
        return vehicle is null ? FleetCatalogueErrors.VehicleNotFound : vehicle;
    }
}

public sealed class GetPublicGalleryHandler(ICatalogueReader catalogue)
    : IRequestHandler<GetPublicGalleryQuery, Result<PublicGallery, Error>>
{
    public async Task<Result<PublicGallery, Error>> Handle(
        GetPublicGalleryQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var gallery = await catalogue.GetGalleryAsync(request.DealerId, cancellationToken);
        return gallery is null ? FleetCatalogueErrors.GalleryNotFound : gallery;
    }
}

public sealed class QuoteRentalHandler(
    IVehicleRepository vehicles,
    IDealerRepository dealers,
    IBookingRepository bookings,
    BookingPricer pricer,
    IBusinessRulesProvider businessRules,
    IReportingCalendar calendar,
    IClock clock)
    : IRequestHandler<QuoteRentalQuery, Result<RentalQuote, Error>>
{
    public async Task<Result<RentalQuote, Error>> Handle(
        QuoteRentalQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = clock.UtcNow;
        if (request.PickupAt <= now)
            return BookingErrors.PeriodInThePast;

        var period = DateRange.Create(request.PickupAt, request.ReturnAt);
        if (period.IsFailure)
            return period.Error;

        var pickupMethod = Enumeration.GetAll<PickupMethod>()
            .FirstOrDefault(method => string.Equals(method.Name, request.PickupMethod, StringComparison.OrdinalIgnoreCase));
        if (pickupMethod is null)
            return Error.Validation("booking.unknown_pickup_method", "Choose either SelfPickup or Delivery.");

        GeoPoint? location = null;
        if (request.Latitude is { } latitude && request.Longitude is { } longitude)
        {
            var point = GeoPoint.Create(latitude, longitude);
            if (point.IsFailure)
                return point.Error;
            location = point.Value;
        }

        var vehicle = await vehicles.GetByIdAsync(request.VehicleId, cancellationToken);
        if (vehicle is null)
            return FleetCatalogueErrors.VehicleNotFound;

        var dealer = await dealers.GetByIdAsync(vehicle.DealerId, cancellationToken);
        // The same silence the listing endpoint keeps: a car whose gallery cannot trade is simply
        // not a car, rather than a car with an explanation an anonymous caller could mine.
        if (dealer is null || !vehicle.IsBookable(dealer.CanTrade))
            return FleetCatalogueErrors.VehicleNotFound;

        var priced = await pricer.PriceAsync(vehicle, dealer, period.Value, pickupMethod, location, cancellationToken);
        if (priced.IsFailure)
            return priced.Error;

        var rules = await businessRules.GetAsync(cancellationToken);
        // The same guard the booking itself will run, so a quote that says "available" and a booking
        // that is refused cannot disagree for any reason other than someone else booking first.
        var taken = await bookings.HasOverlappingBookingAsync(
            request.VehicleId,
            period.Value,
            TimeSpan.FromMinutes(rules.TurnaroundMinutes),
            now,
            excludingBookingId: null,
            cancellationToken);

        var terms = priced.Value.Terms;
        return new RentalQuote(
            vehicle.Id.Value,
            period.Value.Start,
            period.Value.End,
            calendar.TimeZoneId,
            pickupMethod.Name,
            BookingPricingDto.From(priced.Value.Pricing),
            new QuoteTerms(
                terms.DepositPercent.Value,
                terms.FreeCancellationWindow.TotalHours,
                terms.PaymentWindow.TotalMinutes,
                terms.CustomerCancellationPenaltyPercent.Value,
                terms.NoShowTimeout.TotalHours),
            !taken);
    }
}

/// <summary>
/// The catalogue says "no such car" and nothing else.
/// </summary>
/// <remarks>
/// A draft listing, a hidden one, a suspended gallery and a genuinely unknown id all answer the
/// same. These endpoints need no sign-in, and a distinguishable response would let anyone walk a
/// competitor's unpublished inventory by trying ids.
/// </remarks>
public static class FleetCatalogueErrors
{
    public static readonly Error VehicleNotFound =
        Error.NotFound("vehicle.not_found", "This car is not available.");

    public static readonly Error GalleryNotFound =
        Error.NotFound("gallery.not_found", "This rental office is not available.");
}
