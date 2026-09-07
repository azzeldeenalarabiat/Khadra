using CSharpFunctionalExtensions;
using FluentValidation;
using Khadra.Application.Bookings.Dtos;
using Khadra.Application.Bookings.ReadModels;
using Khadra.Application.Common;
using Khadra.Application.Dealers;
using Khadra.Application.Notifications;
using Khadra.Domain.Bookings;
using Khadra.Domain.Bookings.Repositories;
using Khadra.Domain.Common;
using Khadra.Domain.Notifications;
using MediatR;

namespace Khadra.Application.Bookings.DecideBooking;

// The dealer's side of a booking (spec 4.2, 5.4): answer a request, then record the two handovers.
// Every one of these names the person who did it -- owner or employee -- on the booking's own status
// history, which is the accountability record spec 4.2 asks for. Nothing here touches money: an
// approval only opens the customer's window to pay, and cash at handover is recorded as a fact,
// never computed from.

public sealed record ApproveBookingCommand(Id ActorUserId, Id BookingId, string? Note) : ICommand<Result<BookingDto, Error>>;

/// <summary>
/// A closed list of reasons plus free text. The customer reads the result, so the code is turned into
/// words written for them rather than shipped as an identifier.
/// </summary>
public sealed record RejectBookingCommand(Id ActorUserId, Id BookingId, string ReasonCode, string Details)
    : ICommand<Result<BookingDto, Error>>;

public sealed record RecordPickupCommand(
    Id ActorUserId,
    Id BookingId,
    int? OdometerKm,
    decimal? FuelLevel,
    string? Notes,
    decimal? CashCollected) : ICommand<Result<BookingDto, Error>>;

public sealed record RecordReturnCommand(
    Id ActorUserId,
    Id BookingId,
    int? OdometerKm,
    decimal? FuelLevel,
    string? Notes,
    decimal? CashCollected) : ICommand<Result<BookingDto, Error>>;

/// <summary>
/// The closed set of reasons a gallery may decline a request.
/// </summary>
/// <remarks>
/// This was a dictionary of English sentences, and the sentence was composed into the stored reason.
/// The set itself now lives in the domain as <see cref="BookingRejectionReason"/> -- so the CODE is
/// what is persisted -- and both languages of the label travel on <c>GET /api/v1/app-config</c>
/// beside the platform's other vocabularies. This shim keeps the name the validator reads.
/// </remarks>
public static class RejectionReasons
{
    public static bool IsKnown(string? code) => BookingRejectionReason.IsKnown(code);
}

public sealed class ApproveBookingCommandValidator : AbstractValidator<ApproveBookingCommand>
{
    public ApproveBookingCommandValidator() =>
        RuleFor(command => command.Note).MaximumLength(500);
}

public sealed class RejectBookingCommandValidator : AbstractValidator<RejectBookingCommand>
{
    public RejectBookingCommandValidator()
    {
        RuleFor(command => command.ReasonCode)
            .Must(RejectionReasons.IsKnown)
            .WithMessage("Choose one of the listed reasons.");
        RuleFor(command => command.Details).NotEmpty().MaximumLength(500);
    }
}

public sealed class RecordPickupCommandValidator : AbstractValidator<RecordPickupCommand>
{
    public RecordPickupCommandValidator()
    {
        RuleFor(command => command.OdometerKm).GreaterThanOrEqualTo(0).When(command => command.OdometerKm.HasValue);
        RuleFor(command => command.FuelLevel).InclusiveBetween(0m, 1m).When(command => command.FuelLevel.HasValue);
        RuleFor(command => command.Notes).MaximumLength(1000);
        RuleFor(command => command.CashCollected).GreaterThanOrEqualTo(0m).When(command => command.CashCollected.HasValue);
    }
}

public sealed class RecordReturnCommandValidator : AbstractValidator<RecordReturnCommand>
{
    public RecordReturnCommandValidator()
    {
        RuleFor(command => command.OdometerKm).GreaterThanOrEqualTo(0).When(command => command.OdometerKm.HasValue);
        RuleFor(command => command.FuelLevel).InclusiveBetween(0m, 1m).When(command => command.FuelLevel.HasValue);
        RuleFor(command => command.Notes).MaximumLength(1000);
        RuleFor(command => command.CashCollected).GreaterThanOrEqualTo(0m).When(command => command.CashCollected.HasValue);
    }
}

public sealed class BookingDecisionHandlers(
    IBookingRepository bookings,
    DealerMembershipResolver membership,
    IBookingReader reader,
    DealerTeamNotifier team,
    IClock clock,
    IUnitOfWork unitOfWork) :
    IRequestHandler<ApproveBookingCommand, Result<BookingDto, Error>>,
    IRequestHandler<RejectBookingCommand, Result<BookingDto, Error>>,
    IRequestHandler<RecordPickupCommand, Result<BookingDto, Error>>,
    IRequestHandler<RecordReturnCommand, Result<BookingDto, Error>>
{
    public async Task<Result<BookingDto, Error>> Handle(ApproveBookingCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var loaded = await LoadForDecisionAsync(request.ActorUserId, request.BookingId, cancellationToken);
        if (loaded.IsFailure)
            return loaded.Error;

        var approved = loaded.Value.Approve(request.ActorUserId, clock.UtcNow, request.Note);
        if (approved.IsFailure)
            return approved.Error;

        return await CommitAsync(
            loaded.Value,
            request.ActorUserId,
            NotificationKind.BookingApproved,
            cancellationToken,
            NotificationKind.YourBookingApproved);
    }

    public async Task<Result<BookingDto, Error>> Handle(RejectBookingCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var loaded = await LoadForDecisionAsync(request.ActorUserId, request.BookingId, cancellationToken);
        if (loaded.IsFailure)
            return loaded.Error;

        // The CODE and the dealer's own words, kept apart. They used to be composed into one English
        // sentence on the way in, which put untranslatable prose on a permanent record: an
        // Arabic-speaking customer read "The vehicle is no longer available" on their own booking and
        // nothing downstream could do anything about it. Whoever renders it now picks the sentence.
        var reasonCode = Enumeration.GetAll<BookingRejectionReason>()
            .First(reason => string.Equals(reason.Name, request.ReasonCode, StringComparison.Ordinal));
        var rejected = loaded.Value.Reject(request.ActorUserId, reasonCode, request.Details, clock.UtcNow);
        if (rejected.IsFailure)
            return rejected.Error;

        return await CommitAsync(
            loaded.Value,
            request.ActorUserId,
            NotificationKind.BookingRejected,
            cancellationToken,
            NotificationKind.YourBookingRejected);
    }

    public async Task<Result<BookingDto, Error>> Handle(RecordPickupCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var loaded = await LoadForHandoverAsync(request.ActorUserId, request.BookingId, cancellationToken);
        if (loaded.IsFailure)
            return loaded.Error;
        var booking = loaded.Value;

        var recorded = booking.RecordPickup(
            BookingParty.Dealer,
            request.ActorUserId,
            clock.UtcNow,
            odometerKm: request.OdometerKm,
            fuelLevel: request.FuelLevel,
            notes: request.Notes,
            cashCollected: Cash(request.CashCollected, booking));
        if (recorded.IsFailure)
            return recorded.Error;

        return await CommitAsync(booking, request.ActorUserId, NotificationKind.BookingPickedUp, cancellationToken);
    }

    public async Task<Result<BookingDto, Error>> Handle(RecordReturnCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var loaded = await LoadForHandoverAsync(request.ActorUserId, request.BookingId, cancellationToken);
        if (loaded.IsFailure)
            return loaded.Error;
        var booking = loaded.Value;

        var recorded = booking.RecordReturn(
            BookingParty.Dealer,
            request.ActorUserId,
            clock.UtcNow,
            odometerKm: request.OdometerKm,
            fuelLevel: request.FuelLevel,
            notes: request.Notes,
            cashCollected: Cash(request.CashCollected, booking));
        if (recorded.IsFailure)
            return recorded.Error;

        return await CommitAsync(booking, request.ActorUserId, NotificationKind.BookingReturned, cancellationToken);
    }

    /// <summary>
    /// Approve and reject: the person must be able to act for the business AND the business must be
    /// able to trade. The pipeline policy already checked the second; the aggregate checks both again
    /// because a policy is a seam, not the rule.
    /// </summary>
    private async Task<Result<Booking, Error>> LoadForDecisionAsync(Id actorUserId, Id bookingId, CancellationToken cancellationToken)
    {
        var loaded = await LoadOwnedAsync(actorUserId, bookingId, cancellationToken);
        if (loaded.IsFailure)
            return loaded.Error;

        return loaded.Value.Member.CanActOnBookings
            ? loaded.Value.Booking
            : BookingErrors.ActorCannotDecide;
    }

    /// <summary>
    /// Pickup and return: owner or active employee, and deliberately NOT gated on the business being
    /// able to trade. A suspension is a sanction on new business; a booking approved before it, with
    /// a customer holding the dealer's car, still has to be handed over and taken back. (Owner
    /// decision pending on pickup specifically; the default here is to allow it.)
    /// </summary>
    private async Task<Result<Booking, Error>> LoadForHandoverAsync(Id actorUserId, Id bookingId, CancellationToken cancellationToken)
    {
        var loaded = await LoadOwnedAsync(actorUserId, bookingId, cancellationToken);
        return loaded.IsFailure ? loaded.Error : loaded.Value.Booking;
    }

    /// <summary>A booking that belongs to the actor's dealership; not_found otherwise, never 403.</summary>
    private async Task<Result<(Booking Booking, DealerMembership Member), Error>> LoadOwnedAsync(
        Id actorUserId,
        Id bookingId,
        CancellationToken cancellationToken)
    {
        var member = await membership.ResolveAsync(actorUserId, cancellationToken);
        if (member.IsFailure)
            return member.Error;

        var booking = await bookings.GetByIdAsync(bookingId, cancellationToken);
        if (booking is null || booking.DealerId != member.Value.Dealer.Id)
            return BookingErrors.NotFound;

        return (booking, member.Value);
    }

    /// <summary>
    /// Saves the decision and, in the SAME transaction, tells the rest of the dealership about it.
    ///
    /// Staged before the save on purpose. Raising afterwards — or from the domain event this
    /// transition adds — would be at-most-once: `UnitOfWork` dispatches events after the commit, so a
    /// failure there leaves the booking decided and nobody told, with nothing to show it went
    /// missing. Same reasoning the audit trail is built on.
    /// </summary>
    /// <param name="customerKind">
    /// What the CUSTOMER is told, where there is anything to tell them. Null for the handovers: the
    /// customer was standing at the counter when the car changed hands, and a notification about an
    /// event they took part in is noise. It was pre-launch checklist item 60 that nothing told a
    /// customer their booking had been answered at all — the dealer's decision reached them only by
    /// email, and only if they had verified an address.
    /// </param>
    private async Task<Result<BookingDto, Error>> CommitAsync(
        Booking booking,
        Id actorUserId,
        NotificationKind kind,
        CancellationToken cancellationToken,
        NotificationKind? customerKind = null)
    {
        var member = await membership.ResolveAsync(actorUserId, cancellationToken);
        if (member.IsSuccess)
        {
            await team.NotifyTeamAsync(
                member.Value.Dealer,
                actorUserId,
                kind,
                clock.UtcNow,
                booking.Id,
                booking.Reference.Value,
                cancellationToken);

            if (customerKind is not null)
            {
                await team.NotifyCustomerAsync(
                    booking.CustomerId,
                    member.Value.Dealer.BusinessName.Value,
                    customerKind,
                    clock.UtcNow,
                    booking.Id,
                    booking.Reference.Value);
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        var context = await reader.ContextAsync(booking.Id, cancellationToken);
        return BookingDto.From(booking, context, clock.UtcNow);
    }

    // Cash at handover is in the booking's own currency and is recorded, never computed from.
    private static Money? Cash(decimal? amount, Booking booking) =>
        amount is { } cash ? Money.Create(cash, booking.Pricing.CurrencyCode) : null;
}
