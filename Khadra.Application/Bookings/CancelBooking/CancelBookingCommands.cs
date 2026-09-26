using CSharpFunctionalExtensions;
using FluentValidation;
using Khadra.Application.Bookings.Dtos;
using Khadra.Application.Bookings.ReadModels;
using Khadra.Application.Common;
using Khadra.Application.Notifications;
using Khadra.Application.Payments;
using Khadra.Domain.Bookings;
using Khadra.Domain.Bookings.Repositories;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers.Repositories;
using Khadra.Domain.Notifications;
using Khadra.Domain.Payments.Repositories;
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
/// <param name="ExpectedRefund">
/// The refund the customer was shown, in the booking's currency (owner, 2026-09-26). When it no
/// longer matches what cancelling returns — the free window closed while the sheet was open — the
/// cancellation is refused with <c>booking.refund_changed</c> and the current figure, and nothing is
/// cancelled. Optional: an installed app that does not send it is not guarded.
/// </param>
public sealed record CancelMyBookingCommand(Id CustomerUserId, Id BookingId, string ReasonCode, string? Details, decimal? ExpectedRefund = null)
    : ICommand<Result<BookingDto, Error>>;

/// <summary>
/// A customer reporting that the gallery never handed the car over (spec 5.5).
/// </summary>
/// <remarks>
/// It needs a Confirmed booking, which now exists end to end (Sandbox payments). The penalty is
/// assessed against the OFFICE; the customer's money above the deposit goes back at once, in the same
/// save (Phase 3), and the deposit follows the dispute rules.
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
        RuleFor(command => command.ExpectedRefund!.Value)
            .GreaterThanOrEqualTo(0m)
            .When(command => command.ExpectedRefund is not null)
            .WithName("ExpectedRefund")
            .WithMessage("The expected refund cannot be negative.");
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
    IPaymentRepository payments,
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

        // The refund the customer was shown must still be the refund (owner, 2026-09-26). Checked
        // AFTER the retry and lapse branches above, so a retried cancellation that already succeeded
        // is never refused as a change, and only while the booking can be cancelled at all, so a
        // booking past cancelling answers with the aggregate's own reason. Compared as money in the
        // booking's currency, rounded as money is; a promise of something that now returns nothing is
        // a change too.
        if (request.ExpectedRefund is { } expected && booking.CanBeCancelled(now))
        {
            var confirming = booking.DepositPaymentId is { } paymentId
                ? await payments.GetByIdAsync(paymentId, cancellationToken)
                : null;
            var current = BookingEndingRefunds.PreviewForCustomer(booking, confirming, now)
                ?? Money.ZeroIn(booking.Pricing.CurrencyCode);
            if (Money.Create(expected, current.CurrencyCode) != current)
                return BookingErrors.RefundChanged(current);
        }

        var cancelled = booking.Cancel(BookingParty.Customer, request.CustomerUserId, request.Details, now, reasonCode);
        if (cancelled.IsFailure)
            return cancelled.Error;

        // The refund this ending owes — the whole payment inside the free window (owner, 2026-09-24),
        // everything above the deposit after it (Phase 3) — recorded in THIS save so the cancellation
        // and the refund commit together or not at all. The retry branch above deliberately does not
        // do this: a refund is only ever created by the ending that owes it, never by tapping again.
        await BookingEndingRefunds.RecordAsync(booking, payments, now, cancellationToken);

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

        // The car never came: everything the customer paid above the deposit goes back now (Phase 3),
        // in this save. The deposit follows the dispute rules.
        await BookingEndingRefunds.RecordAsync(booking, payments, now, cancellationToken);

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
        BookingDto.From(booking, await reader.ContextAsync(booking.Id, cancellationToken), now).ForCustomer();
}
