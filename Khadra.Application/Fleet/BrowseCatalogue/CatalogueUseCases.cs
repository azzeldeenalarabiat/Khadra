using CSharpFunctionalExtensions;
using Khadra.Application.Bookings;
using Khadra.Application.Bookings.Dtos;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.Fleet.ReadModels;
using Khadra.Application.Reviews.ReadModels;
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
    PageRequest Page,
    // Added for the customer website (2026-09-23), all optional so the app's requests mean what they did.
    string? FuelType = null,
    string? Make = null,
    int? MinYear = null,
    int? MaxYear = null,
    string? Sort = null) : IQuery<Result<PagedResult<CatalogueListing>, Error>>;

/// <summary>The rental offices a customer may be shown, optionally in one city.</summary>
/// <remarks>
/// <c>Text</c> and <c>DeliveryOnly</c> were added for the customer website's office search
/// (2026-09-24), optional and defaulted, so a request without them means what it always did. They are
/// named as the car search names its own, so one client vocabulary serves both lists.
/// </remarks>
/// <param name="Text">Part of the office's business name, matched without case. Blank means no filter.</param>
/// <param name="DeliveryOnly">
/// Only offices that have delivery switched on — the same flag the card's delivery badge reads, so a
/// filtered list never shows a card that says it does not deliver.
/// </param>
public sealed record ListPublicGalleriesQuery(
    Guid? CityId,
    PageRequest Page,
    string? Text = null,
    bool DeliveryOnly = false)
    : IQuery<Result<PagedResult<PublicGalleryCard>, Error>>;

/// <summary>One car's own page.</summary>
/// <param name="Language">
/// The reader's language, filled by the controller from `Accept-Language`. A FIELD rather than a
/// port the handler injects, so a test writes `Language.Arabic` and nothing below this line has to
/// know an HTTP request exists.
/// </param>
public sealed record GetCatalogueVehicleQuery(
    Id VehicleId,
    DateTimeOffset? PickupAt,
    DateTimeOffset? ReturnAt,
    Language Language)
    : IQuery<Result<CatalogueVehicle, Error>>;

/// <summary>A gallery's public page.</summary>
public sealed record GetPublicGalleryQuery(Id DealerId, Language Language)
    : IQuery<Result<PublicGalleryPage, Error>>;

/// <summary>The seat counts and car types the bookable catalogue holds, for building its filters.</summary>
public sealed record GetCatalogueFacetsQuery : IQuery<Result<CatalogueFacets, Error>>;

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
    double PaymentWindowHours,
    decimal CustomerCancellationPenaltyPercent,
    double NoShowTimeoutHours,
    /// <summary>
    /// How long the gallery would have to answer this request.
    /// </summary>
    /// <remarks>
    /// The customer is agreeing to wait this long with a car held for them, so it belongs in the
    /// terms they are shown before agreeing. It was missing, and the app had nothing to state it
    /// from until a booking existed to subtract two of its timestamps.
    /// </remarks>
    double AnswerWindowHours);

public sealed class SearchCatalogueHandler(
    ICatalogueReader catalogue,
    IGalleryReviewReader reviews,
    IBusinessRulesProvider businessRules,
    IReportingCalendar calendar,
    IClock clock)
    : IRequestHandler<SearchCatalogueQuery, Result<PagedResult<CatalogueListing>, Error>>
{
    public async Task<Result<PagedResult<CatalogueListing>, Error>> Handle(
        SearchCatalogueQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // An order nobody offered is a client bug, not a browse: refused, the way half a period is,
        // rather than quietly answered in some other order the customer did not ask for.
        CatalogueSort? sort = null;
        if (!string.IsNullOrWhiteSpace(request.Sort))
        {
            sort = Enumeration.GetAll<CatalogueSort>()
                .FirstOrDefault(candidate => string.Equals(candidate.Name, request.Sort, StringComparison.OrdinalIgnoreCase));
            if (sort is null)
            {
                return Error.Validation(
                    "catalogue.unknown_sort",
                    "Sort by Newest, PriceLowToHigh, PriceHighToLow or YearNewest.");
            }
        }

        if (request.MinYear is { } minYear && request.MaxYear is { } maxYear && minYear > maxYear)
            return Error.Validation("catalogue.inverted_years", "The earliest year is after the latest one.");

        var window = await BuildWindowAsync(
            request.PickupAt, request.ReturnAt, businessRules, calendar, clock, cancellationToken);
        if (window.IsFailure)
            return window.Error;

        Id? cityId = request.CityId is { } city ? Id.From(city) : null;
        Id? dealerId = request.DealerId is { } dealer ? Id.From(dealer) : null;
        var collection = window.Value is { } dated
            ? await CollectionAtAsync(dated, cityId, dealerId, cancellationToken)
            : null;

        var filter = new CatalogueFilter(
            cityId,
            request.CarTypeId is { } carType ? Id.From(carType) : null,
            dealerId,
            request.MinDailyRate,
            request.MaxDailyRate,
            request.Transmission,
            request.MinSeats,
            request.DeliveryOnly,
            request.Text,
            window.Value,
            request.FuelType,
            request.Make,
            request.MinYear,
            request.MaxYear,
            sort,
            collection);

        var page = await catalogue.SearchAsync(filter, request.Page, cancellationToken);

        // The rating is the review context's answer, judged at now, never the catalogue's — see
        // CatalogueRatings. Awaited after the search, not beside it: both readers share one DbContext.
        var ratings = await CatalogueRatings.ForAsync(
            reviews, page.Items.Select(item => item.Gallery.DealerId), clock.UtcNow, cancellationToken);
        var open = collection?.OpenForSelfPickup.Select(id => id.Value).ToHashSet();
        return page with
        {
            Items =
            [
                .. page.Items.Select(item => CatalogueRatings.Apply(item, ratings) with
                {
                    SelfPickupAvailable = open?.Contains(item.Gallery.DealerId),
                }),
            ],
        };
    }

    /// <summary>
    /// Which offices can hand a car over at the searched times (Wave 3 E7; E2E F2): open at both the local pickup and the
    /// local return time, judged by <see cref="PickupHoursPolicy"/> exactly as the quote judges them, or delivering.
    /// </summary>
    /// <remarks>
    /// Checklist 66 once kept hours out of the search because a search spans offices with no single schedule. Each office
    /// is judged on its own here instead, so search and quote cannot disagree: a car the search lists is one the quote
    /// prices, by self-pickup or by delivery. Awaited before the search, never beside it: the readers share a DbContext.
    /// </remarks>
    private async Task<CollectionRule> CollectionAtAsync(
        AvailabilityWindow window,
        Id? cityId,
        Id? dealerId,
        CancellationToken cancellationToken)
    {
        var schedules = await catalogue.OfficeSchedulesAsync(cityId, dealerId, cancellationToken);
        var start = window.Period.Start;
        var end = window.Period.End;
        var open = schedules
            .Where(schedule => PickupHoursPolicy.Validate(
                schedule.Hours,
                PickupMethod.SelfPickup,
                calendar.DayOf(start),
                calendar.TimeOfDay(start),
                calendar.DayOf(end),
                calendar.TimeOfDay(end)).IsSuccess)
            .Select(schedule => schedule.DealerId)
            .ToList();
        var delivering = schedules.Where(schedule => schedule.DeliveryEnabled).Select(schedule => schedule.DealerId).ToList();
        return new CollectionRule(open, delivering);
    }

    /// <summary>
    /// Turns an optional pair of instants into an availability window, or into nothing.
    /// </summary>
    /// <remarks>
    /// Shared by search and the single listing because both accept the same optional dates and must
    /// judge them the same way. Half a period is a client bug, not a browse: silently ignoring one
    /// end would show a customer cars that are not free on the dates they typed.
    ///
    /// Dates that could never be booked are refused rather than searched. It would be easy to argue
    /// that browsing should be permissive and only the booking strict, and it is wrong: a search for
    /// a car in the next half hour would list cars, price them, and refuse at the last step. Every
    /// surface that takes rental dates judges them through BookingWindowPolicy, so the customer is
    /// told about a date they cannot use at the moment they type it.
    /// </remarks>
    internal static async Task<Result<AvailabilityWindow?, Error>> BuildWindowAsync(
        DateTimeOffset? pickupAt,
        DateTimeOffset? returnAt,
        IBusinessRulesProvider businessRules,
        IReportingCalendar calendar,
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
        var now = clock.UtcNow;

        var window = BookingWindowPolicy.Validate(
            period.Value,
            now,
            TimeSpan.FromMinutes(rules.MinimumBookingLeadTimeMinutes),
            rules.MaxAdvanceBookingDays,
            BilledDays(period.Value, calendar),
            rules.MaxRentalDays);
        if (window.IsFailure)
            return window.Error;

        return new AvailabilityWindow(
            period.Value,
            now,
            TimeSpan.FromMinutes(rules.TurnaroundMinutes));
    }

    /// <summary>
    /// The days this rental would be BILLED for: the Amman calendar dates, never the elapsed hours.
    /// </summary>
    /// <remarks>
    /// The one conversion in this file, so search, the single listing and the quote all bound the
    /// length by the same count the price is built from. <c>BookingPricer</c> does the same
    /// conversion for the figures it freezes; they cannot disagree, because both go through
    /// <c>IReportingCalendar</c> and <c>RentalDays</c>.
    /// </remarks>
    internal static int BilledDays(DateRange period, IReportingCalendar calendar) =>
        RentalDays.Between(calendar.DayOf(period.Start), calendar.DayOf(period.End));
}

public sealed class GetCatalogueVehicleHandler(
    ICatalogueReader catalogue,
    IGalleryReviewReader reviews,
    IBusinessRulesProvider businessRules,
    IReportingCalendar calendar,
    IClock clock)
    : IRequestHandler<GetCatalogueVehicleQuery, Result<CatalogueVehicle, Error>>
{
    public async Task<Result<CatalogueVehicle, Error>> Handle(
        GetCatalogueVehicleQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var window = await SearchCatalogueHandler.BuildWindowAsync(
            request.PickupAt, request.ReturnAt, businessRules, calendar, clock, cancellationToken);
        if (window.IsFailure)
            return window.Error;

        var vehicle = await catalogue.GetAsync(request.VehicleId, window.Value, request.Language, cancellationToken);
        if (vehicle is null)
            return FleetCatalogueErrors.VehicleNotFound;

        // The embedded gallery's rating, from the one definition that honours the blind window.
        var ratings = await CatalogueRatings.ForAsync(
            reviews, [vehicle.Gallery.DealerId], clock.UtcNow, cancellationToken);
        return CatalogueRatings.Apply(vehicle, ratings);
    }
}

public sealed class GetPublicGalleryHandler(
    ICatalogueReader catalogue,
    IGalleryReviewReader reviews,
    IClock clock)
    : IRequestHandler<GetPublicGalleryQuery, Result<PublicGalleryPage, Error>>
{
    public async Task<Result<PublicGalleryPage, Error>> Handle(
        GetPublicGalleryQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var gallery = await catalogue.GetGalleryAsync(request.DealerId, request.Language, cancellationToken);
        if (gallery is null)
            return FleetCatalogueErrors.GalleryNotFound;

        // Through the same blind-window test the page's review list applies, so the count in the
        // header and the reviews listed underneath it cannot disagree.
        var ratings = await CatalogueRatings.ForAsync(
            reviews, [gallery.DealerId], clock.UtcNow, cancellationToken);
        return CatalogueRatings.Apply(gallery, ratings);
    }
}

public sealed class ListPublicGalleriesHandler(
    ICatalogueReader catalogue,
    IGalleryReviewReader reviews,
    IClock clock)
    : IRequestHandler<ListPublicGalleriesQuery, Result<PagedResult<PublicGalleryCard>, Error>>
{
    public async Task<Result<PagedResult<PublicGalleryCard>, Error>> Handle(
        ListPublicGalleriesQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var filter = new GalleryDirectoryFilter(
            request.CityId is { } city ? Id.From(city) : null,
            request.Text,
            request.DeliveryOnly);
        var page = await catalogue.ListGalleriesAsync(filter, request.Page, cancellationToken);

        var ratings = await CatalogueRatings.ForAsync(
            reviews, page.Items.Select(card => card.DealerId), clock.UtcNow, cancellationToken);
        return page with { Items = [.. page.Items.Select(card => CatalogueRatings.Apply(card, ratings))] };
    }
}

public sealed class GetCatalogueFacetsHandler(ICatalogueReader catalogue)
    : IRequestHandler<GetCatalogueFacetsQuery, Result<CatalogueFacets, Error>>
{
    public async Task<Result<CatalogueFacets, Error>> Handle(
        GetCatalogueFacetsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await catalogue.FacetsAsync(cancellationToken);
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
        var rules = await businessRules.GetAsync(cancellationToken);

        var period = DateRange.Create(request.PickupAt, request.ReturnAt);
        if (period.IsFailure)
            return period.Error;

        // The same policy the create endpoint applies, so a quote can never price a rental the
        // booking would then refuse. It replaces a bare "must be in the future" check that let a
        // customer be quoted for a pickup twenty minutes away, or for a date past the horizon.
        var window = BookingWindowPolicy.Validate(
            period.Value,
            now,
            TimeSpan.FromMinutes(rules.MinimumBookingLeadTimeMinutes),
            rules.MaxAdvanceBookingDays,
            SearchCatalogueHandler.BilledDays(period.Value, calendar),
            rules.MaxRentalDays);
        if (window.IsFailure)
            return window.Error;

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
                terms.PaymentWindow.TotalHours,
                terms.CustomerCancellationPenaltyPercent.Value,
                terms.NoShowTimeout.TotalHours,
                terms.AnswerWindow.TotalHours),
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
