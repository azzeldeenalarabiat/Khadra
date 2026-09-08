using CSharpFunctionalExtensions;
using FluentValidation;
using Khadra.Application.Bookings.Dtos;
using Khadra.Application.Bookings.ReadModels;
using Khadra.Application.Common;
using Khadra.Application.Notifications;
using Khadra.Domain.Bookings;
using Khadra.Domain.Bookings.Repositories;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers.Repositories;
using Khadra.Domain.Notifications;
using MediatR;

namespace Khadra.Application.Bookings.CancelBooking;

/// <summary>
/// A customer ending their own booking (spec 5.5).
/// </summary>
/// <remarks>
/// The party is fixed in the handler and is never a field on the request. Attribution decides the
/// penalty — <c>AssessCancellation</c> charges a customer the frozen percentage of the deposit and a
/// dealer a percentage of the rental — so a client that could name the party could name the cheaper
/// one, or blame the other side.
/// </remarks>
/// <param name="ReasonCode">One of <see cref="BookingCancellationReason"/>, published on /app-config.</param>
/// <param name="Details">The customer's own words. Optional; the code is what the platform counts.</param>
public sealed record CancelMyBookingCommand(Id CustomerUserId, Id BookingId, string ReasonCode, string? Details)
    : ICommand<Result<BookingDto, Error>>;

/// <summary>
/// A customer reporting that the gallery never handed the car over (spec 5.5).
/// </summary>
/// <remarks>
/// NOT REACHABLE END TO END. It requires a Confirmed booking, and Confirmed requires a cleared
/// deposit, which requires the Payments context — unbuilt and blocked on owner decisions. The command
/// and its guards exist so the aggregate rule is enforced and tested wherever it is asked; the app
/// gates the screen on the booking's own status, so a customer never sees a button that cannot work.
/// </remarks>
public sealed record ReportNonDeliveryCommand(Id CustomerUserId, Id BookingId, string Details)
    : ICommand<Result<BookingDto, Error>>;

public sealed class CancelMyBookingCommandValidator : AbstractValidator<CancelMyBookingCommand>
{
    public CancelMyBookingCommandValidator()
    {
        RuleFor(command => command.ReasonCode)
            .Must(BookingCancellationReason.IsKnown)
            .WithMessage("Choose one of the listed reasons.");
        RuleFor(command => command.Details).MaximumLength(500);
    }
}

public sealed class ReportNonDeliveryCommandValidator : AbstractValidator<ReportNonDeliveryCommand>
{
    public ReportNonDeliveryCommandValidator() =>
        RuleFor(command => command.Details).NotEmpty().MaximumLength(1000);
}

/// <summary>
/// The two ways a customer ends a booking themselves.
/// </summary>
/// <remarks>
/// Neither takes <c>IVehicleHoldLock</c> and neither opens a transaction of its own. That lock exists
/// so a CREATOR's answer is truthful — it serialises expire-then-check-then-insert on one vehicle, so
/// two customers cannot both clear the same stale hold and one be refused over dates that were free.
/// Ending a booking only ever RELEASES a hold: it removes the row from the partial index behind
/// <c>bookings_one_hold_per_vehicle</c>, so it cannot collide with that constraint, and it reads no
/// availability. The races that do exist — the gallery approving at the same instant, another
/// customer's creation expiring this row as stale — are settled by the optimistic concurrency token
/// on <c>bookings</c>, which surfaces as a 409 the client answers by reloading.
/// </remarks>
public sealed class CancelBookingHandlers(
    IBookingRepository bookings,
    IBookingReader reader,
    IDealerRepository dealers,
    DealerTeamNotifier team,
    IClock clock,
    IUnitOfWork unitOfWork) :
    IRequestHandler<CancelMyBookingCommand, Result<BookingDto, Error>>,
    IRequestHandler<ReportNonDeliveryCommand, Result<BookingDto, Error>>
{
    public async Task<Result<BookingDto, Error>> Handle(
        CancelMyBookingCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var loaded = await LoadMineAsync(request.CustomerUserId, request.BookingId, cancellationToken);
        if (loaded.IsFailure)
            return loaded.Error;

        var booking = loaded.Value;
        var now = clock.UtcNow;

        // A phone retries a request whose response it never saw. Answering the second attempt with
        // "this can no longer be cancelled" would tell the customer their cancellation failed when
        // it succeeded. The aggregate stays strict — an admin double-cancelling is still refused —
        // and the retry is recognised here, where the caller's identity is known.
        if (booking.Status == BookingStatus.Cancelled && booking.CancelledBy == BookingParty.Customer)
            return await DescribeAsync(booking, now, cancellationToken);

        // A window that closed already decided this booking; only the row is behind. Recording a
        // cancellation over it would say "you cancelled" where the truth is "the time ran out" —
        // a distinction docs/spec-amendments.md insists the customer can make, and the one the
        // gallery would otherwise be told a customer walked away from a request that had lapsed on
        // their own clock. Financially identical today, since both assess nothing; but this is the
        // row a dispute is read from.
        if (booking.HasLapsed(now))
        {
            var expired = booking.Status == BookingStatus.Requested
                ? booking.ExpireUnanswered(now)
                : booking.ExpireUnpaid(now);
            if (expired.IsFailure)
                return expired.Error;

            await unitOfWork.SaveChangesAsync(cancellationToken);
            return await DescribeAsync(booking, now, cancellationToken);
        }

        var reasonCode = Enumeration.GetAll<BookingCancellationReason>()
            .First(reason => string.Equals(reason.Name, request.ReasonCode, StringComparison.Ordinal));

        var cancelled = booking.Cancel(BookingParty.Customer, request.CustomerUserId, request.Details, now, reasonCode);
        if (cancelled.IsFailure)
            return cancelled.Error;

        await NotifyGalleryAsync(booking, NotificationKind.BookingCancelledByCustomer, now, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await DescribeAsync(booking, now, cancellationToken);
    }

    public async Task<Result<BookingDto, Error>> Handle(
        ReportNonDeliveryCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var loaded = await LoadMineAsync(request.CustomerUserId, request.BookingId, cancellationToken);
        if (loaded.IsFailure)
            return loaded.Error;

        var booking = loaded.Value;
        var now = clock.UtcNow;

        var reported = booking.ReportDealerNonDelivery(request.CustomerUserId, request.Details, now);
        if (reported.IsFailure)
            return reported.Error;

        await NotifyGalleryAsync(booking, NotificationKind.BookingNonDeliveryReported, now, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await DescribeAsync(booking, now, cancellationToken);
    }

    /// <summary>
    /// The caller's own booking, or <c>not_found</c>.
    /// </summary>
    /// <remarks>
    /// Never 403. A booking that exists but belongs to somebody else answers exactly as one that does
    /// not exist, because a 403 confirms the id is real and booking ids are the one thing a stranger
    /// must not be able to enumerate. Same rule as <c>BookingPartyResolver</c>.
    /// </remarks>
    private async Task<Result<Booking, Error>> LoadMineAsync(
        Id customerUserId,
        Id bookingId,
        CancellationToken cancellationToken)
    {
        var booking = await bookings.GetByIdAsync(bookingId, cancellationToken);
        return booking is null || booking.CustomerId != customerUserId
            ? BookingErrors.NotFound
            : booking;
    }

    /// <summary>
    /// Tells the gallery, in the same transaction that records the customer's decision.
    /// </summary>
    /// <remarks>
    /// Staged before the save, not raised from the domain event: <c>UnitOfWork</c> dispatches events
    /// AFTER the commit, so a failure there would leave the booking cancelled and the gallery still
    /// expecting the customer, with nothing to show the message went missing. A gallery that has
    /// since been removed is not worth failing a cancellation over.
    /// </remarks>
    private async Task NotifyGalleryAsync(
        Booking booking,
        NotificationKind kind,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var dealer = await dealers.GetByIdAsync(booking.DealerId, cancellationToken);
        if (dealer is null)
            return;

        await team.NotifyTeamOfCustomerActionAsync(
            dealer, kind, now, booking.Id, booking.Reference.Value);
    }

    private async Task<BookingDto> DescribeAsync(Booking booking, DateTimeOffset now, CancellationToken cancellationToken) =>
        BookingDto.From(booking, await reader.ContextAsync(booking.Id, cancellationToken), now);
}
