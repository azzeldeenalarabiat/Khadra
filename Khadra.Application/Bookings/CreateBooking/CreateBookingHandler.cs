using CSharpFunctionalExtensions;
using Khadra.Application.Bookings.Dtos;
using Khadra.Application.Bookings.ReadModels;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.Notifications;
using Khadra.Domain.Bookings;
using Khadra.Domain.Bookings.Repositories;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers.Repositories;
using Khadra.Domain.Fleet.Repositories;
using Khadra.Domain.IdentityAccess.Repositories;
using Khadra.Domain.Notifications;
using MediatR;

namespace Khadra.Application.Bookings.CreateBooking;

/// <summary>
/// Creates a booking in <c>Requested</c>, holding the vehicle until the dealer's answer window closes.
/// </summary>
/// <remarks>
/// This is deliberately <c>QuoteRentalHandler</c> plus a write. Every guard it runs, the quote runs
/// too, in the same order and through the same code: <c>BookingWindowPolicy</c> for the dates,
/// <c>BookingPricer</c> for delivery eligibility, the gallery's radius and every frozen figure, and
/// <c>HasOverlappingBookingAsync</c> for availability. Anything that diverged between them would be
/// a screen showing a price beside a button that cannot work.
///
/// What it adds are the things a quote has no opinion about, because a quote is anonymous: who the
/// customer is, whether they may book at all, serialising against other creators of the same car,
/// clearing the stale holds standing in the way, the write itself, and telling the gallery that
/// somebody is waiting on them.
/// </remarks>
public sealed class CreateBookingHandler(
    IUserRepository users,
    IVehicleRepository vehicles,
    IDealerRepository dealers,
    IBookingRepository bookings,
    IBookingReader reader,
    BookingPricer pricer,
    IBusinessRulesProvider businessRules,
    IReportingCalendar calendar,
    IVehicleHoldLock vehicleLock,
    DealerTeamNotifier team,
    IUnitOfWork unitOfWork,
    IClock clock)
    : IRequestHandler<CreateBookingCommand, Result<BookingDto, Error>>
{
    public async Task<Result<BookingDto, Error>> Handle(
        CreateBookingCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = clock.UtcNow;
        var rules = await businessRules.GetAsync(cancellationToken);

        var period = DateRange.Create(request.PickupAt, request.ReturnAt);
        if (period.IsFailure)
            return period.Error;

        var window = BookingWindowPolicy.Validate(
            period.Value,
            now,
            TimeSpan.FromMinutes(rules.MinimumBookingLeadTimeMinutes),
            rules.MaxAdvanceBookingDays,
            RentalDays.Between(calendar.DayOf(period.Value.Start), calendar.DayOf(period.Value.End)),
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

        // WHO is asking, before what they are asking for. A customer who may not book at all should
        // not have a car and a gallery loaded to be told so, and should certainly not learn from the
        // shape of the answer whether a given vehicle id exists.
        var customer = await users.GetByIdAsync(request.CustomerUserId, cancellationToken);
        // A signed-in caller whose account has vanished. Unreachable behind the security stamp, and
        // answered as an account problem rather than as a missing booking, because there is no
        // booking in this story yet.
        if (customer is null)
            return BookingErrors.AccountCannotBook;

        var eligible = CheckRenterEligibility(customer);
        if (eligible.IsFailure)
            return eligible.Error;

        var vehicle = await vehicles.GetByIdAsync(request.VehicleId, cancellationToken);
        if (vehicle is null)
            return FleetNotFound;

        var dealer = await dealers.GetByIdAsync(vehicle.DealerId, cancellationToken);
        // The same silence the catalogue keeps. A car whose gallery cannot trade is simply not a
        // car, rather than a car with an explanation someone could mine by trying ids.
        if (dealer is null || !vehicle.IsBookable(dealer.CanTrade))
            return FleetNotFound;

        // Delivery eligibility, the gallery's radius and its own fee are all decided in here, along
        // with every figure the booking freezes. The customer sends no prices.
        var priced = await pricer.PriceAsync(vehicle, dealer, period.Value, pickupMethod, location, cancellationToken);
        if (priced.IsFailure)
            return priced.Error;

        var turnaround = TimeSpan.FromMinutes(rules.TurnaroundMinutes);

        // Built BEFORE the transaction, deliberately. Create is a pure function of arguments already
        // in hand, so there is nothing to gain from building it inside -- and building it outside
        // means the delegate adds an aggregate it did not construct, which is what makes it safe to
        // run twice if EF's retry-on-failure is ever enabled (checklist item 65).
        var booking = Booking.Create(
            customer.Id,
            dealer.Id,
            vehicle.Id,
            period.Value,
            pickupMethod,
            location,
            priced.Value.Pricing,
            priced.Value.Terms,
            PaymentOption.DepositOnly,
            now);
        if (booking.IsFailure)
            return booking.Error;

        var taken = false;

        try
        {
            // ONE transaction, and the order inside it is the whole point of this handler: lock,
            // expire, ask, insert. See ExpireStaleHoldsAsync for why the expiry has to come before
            // the guard, and IVehicleHoldLock for why the lock has to come before the expiry.
            await unitOfWork.ExecuteInTransactionAsync(
                async token =>
                {
                    taken = false;

                    // Everything after this point is the only such sequence running for this car, so
                    // the world it reads has stopped moving under it.
                    await vehicleLock.AcquireAsync(request.VehicleId.Value, token);

                    await ExpireStaleHoldsAsync(request.VehicleId, period.Value, turnaround, now, token);

                    taken = await bookings.HasOverlappingBookingAsync(
                        request.VehicleId, period.Value, turnaround, now, excludingBookingId: null, token);
                    if (taken)
                        return;

                    await bookings.AddAsync(booking.Value, token);

                    // Staged BEFORE the save, in this transaction, so the request and the alert about
                    // it land together or not at all. Raising it afterwards -- or from the domain
                    // event, which dispatches after the commit -- would be at-most-once: a failure
                    // there would leave a request holding a car with nobody at the gallery told, and
                    // nothing to show the alert went missing. The same reasoning the audit trail and
                    // the dealer's decision notifications are built on.
                    await team.NotifyTeamOfCustomerActionAsync(
                        dealer,
                        NotificationKind.BookingRequested,
                        now,
                        booking.Value.Id,
                        booking.Value.Reference.Value);

                    await unitOfWork.SaveChangesAsync(token);
                },
                cancellationToken);
        }
        // The floor under the guard above, and under the lock: only the database can settle a race
        // it did not serialise, and it just did.
        //
        // A ConcurrencyConflictException is deliberately NOT caught here. It would mean somebody
        // else changed a stale hold this transaction was expiring -- an administrator, or the expiry
        // job when it exists -- which says nothing about whether the car is free. Reporting it as
        // "no longer free for those dates" would send a customer away from dates that are fine. The
        // generic conflict, which tells them to reload and try again, is the true answer.
        catch (ExclusiveHoldConflictException exception) when (exception.IsVehicleHold)
        {
            return BookingErrors.VehicleUnavailable;
        }

        if (taken)
            return BookingErrors.VehicleUnavailable;

        var context = await reader.ContextAsync(booking.Value.Id, cancellationToken);
        return BookingDto.From(booking.Value, context, now);
    }

    /// <summary>
    /// Settles the bookings on this vehicle whose hold has run out, in the caller's transaction.
    /// </summary>
    /// <remarks>
    /// Not housekeeping — a correctness requirement, and the reason this handler needs a transaction
    /// at all.
    ///
    /// The database's exclusion constraint cannot mention <c>now()</c>, so a request past its
    /// decision deadline and an approval past its payment deadline both still occupy its index.
    /// <c>BookingHolds.Live</c>, which the guard and the catalogue use, correctly ignores both. Left
    /// alone the two disagree: the guard says the car is free, the customer is told it is free, and
    /// the INSERT is refused by the database.
    ///
    /// This runs BEFORE the guard. It does not change the guard's answer -- the guard already
    /// ignores these rows -- but it does change the database's, and the point is that afterwards
    /// both describe the same world.
    ///
    /// Neither expiry can refuse. The query's predicate and the aggregate's guards are exact
    /// complements against the same <c>now</c> -- the query asks for Requested past its decision
    /// deadline or Approved past its payment deadline, and those are precisely the states in which
    /// the two methods succeed. A refusal would mean the repository and the domain had drifted
    /// apart, which is a programming error and not something to swallow, so it throws.
    ///
    /// One consequence worth being honest about: this customer's request settles other people's
    /// bookings as a side effect, and nobody is told. Those bookings are genuinely over -- their own
    /// clock ended them, not this customer -- but the notification belongs to the background job
    /// that does not exist yet (pre-launch items 4 and 60). Narrowing the query to holds that
    /// overlap this candidate keeps that side effect to the rows that actually stand in the way.
    /// </remarks>
    private async Task ExpireStaleHoldsAsync(
        Id vehicleId,
        DateRange candidatePeriod,
        TimeSpan turnaroundBuffer,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var stale = await bookings.ListStaleHoldsForVehicleAsync(
            vehicleId, candidatePeriod, turnaroundBuffer, now, cancellationToken);
        if (stale.Count == 0)
            return;

        foreach (var booking in stale)
        {
            // No actor. The clock ended these, not the customer who happened to arrive next, and the
            // status history should not name them as though they had.
            var expired = booking.Status == BookingStatus.Requested
                ? booking.ExpireUnanswered(now)
                : booking.ExpireUnpaid(now);

            if (expired.IsFailure)
            {
                throw new DomainException(
                    $"A stale hold the repository selected could not be expired ({expired.Error.Code}). " +
                    "ListStaleHoldsForVehicleAsync and the aggregate's expiry guards have drifted apart.");
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Spec 5.1, and one rule the platform adds because it cannot yet reach the customer any other way.
    /// </summary>
    /// <remarks>
    /// The document rule is a CHECKBOX, not a check: it asks only that the files have been uploaded.
    /// Nothing can move a document out of <c>PendingReview</c>, and no endpoint lets the dealer open
    /// one, so requiring verification would mean nobody could book at all. The owner has accepted
    /// that for development and recorded the dealer document-view endpoint as a hard requirement
    /// before real launch (pre-launch checklist item 63).
    /// </remarks>
    private static UnitResult<Error> CheckRenterEligibility(Domain.IdentityAccess.User customer)
    {
        if (customer.Role != Domain.IdentityAccess.UserRole.Customer)
            return UnitResult.Failure(BookingErrors.AccountCannotBook);

        if (customer.Status != Domain.IdentityAccess.UserStatus.Active)
            return UnitResult.Failure(BookingErrors.AccountCannotBook);

        // Until push notifications exist, an approval reaches a customer only by email. An
        // unverified address is a booking that expires unread, with the gallery's decision wasted
        // and the car held for nothing meanwhile.
        if (!customer.IsEmailVerified)
            return UnitResult.Failure(BookingErrors.EmailNotVerified);

        if (!customer.HasCompleteRenterDocuments)
            return UnitResult.Failure(BookingErrors.RenterDocumentsIncomplete);

        return UnitResult.Success<Error>();
    }

    // The catalogue's own "no such car", repeated here rather than referenced across the context
    // boundary, so a customer probing ids learns the same nothing from both endpoints.
    private static readonly Error FleetNotFound =
        Error.NotFound("vehicle.not_found", "This car is not available.");
}
