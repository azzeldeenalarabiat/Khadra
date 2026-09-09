using CSharpFunctionalExtensions;
using FluentValidation;
using Khadra.Application.Bookings.Dtos;
using Khadra.Application.Bookings.ReadModels;
using Khadra.Application.Common;
using Khadra.Application.Dealers;
using Khadra.Application.Payments;
using Khadra.Domain.Bookings;
using Khadra.Domain.Bookings.Repositories;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Domain.IdentityAccess;
using MediatR;

namespace Khadra.Application.Bookings.ReadBookings;

// The read-only bookings slice (spec 3.3 needs a booking to open a dispute FROM; spec 5 needs a
// customer to see what they booked). Deliberately no creation and no transitions: those belong to the
// Booking module proper, with the payment and overlap decisions it has to make. This slice only lets
// the two parties SEE the bookings the seeder -- and one day the real flow -- put there.

/// <summary>The signed-in person's bookings: theirs as a customer, or their dealership's as staff.</summary>
public sealed record ListMyBookingsQuery(Id UserId, UserRole Role, string? Status, PageRequest Page, string? Tab = null, Guid? VehicleId = null)
    : IQuery<Result<PagedResult<BookingListItem>, Error>>;

/// <summary>How many bookings sit behind each of the caller's tabs.</summary>
public sealed record GetMyBookingTabCountsQuery(Id UserId, UserRole Role)
    : IQuery<Result<IReadOnlyDictionary<string, int>, Error>>;

public sealed class ListMyBookingsQueryValidator : AbstractValidator<ListMyBookingsQuery>
{
    public ListMyBookingsQueryValidator()
    {
        RuleFor(query => query.Status)
            .Must(status => status is null ||
                            Enumeration.GetAll<BookingStatus>().Any(candidate =>
                                string.Equals(candidate.Name, status, StringComparison.OrdinalIgnoreCase)))
            .WithMessage("Unknown booking status.");
        RuleFor(query => query.Tab)
            .Must(BookingTabs.IsKnown)
            .WithMessage("Unknown bookings tab.");
    }
}

public sealed class ListMyBookingsHandler(IBookingReader reader, DealerMembershipResolver membership) :
    IRequestHandler<ListMyBookingsQuery, Result<PagedResult<BookingListItem>, Error>>,
    IRequestHandler<GetMyBookingTabCountsQuery, Result<IReadOnlyDictionary<string, int>, Error>>
{
    public async Task<Result<PagedResult<BookingListItem>, Error>> Handle(
        ListMyBookingsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var scope = await ScopeAsync(request.UserId, request.Role, cancellationToken);
        if (scope.IsFailure)
            return scope.Error;

        var filter = scope.Value with { Status = request.Status, Tab = request.Tab, VehicleId = request.VehicleId };
        return await reader.ListAsync(filter, request.Page, cancellationToken);
    }

    public async Task<Result<IReadOnlyDictionary<string, int>, Error>> Handle(
        GetMyBookingTabCountsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var scope = await ScopeAsync(request.UserId, request.Role, cancellationToken);
        if (scope.IsFailure)
            return scope.Error;

        return Result.Success<IReadOnlyDictionary<string, int>, Error>(
            await reader.TabCountsAsync(scope.Value, cancellationToken));
    }

    /// <summary>Whose bookings: the customer's own, or the dealership the staff member belongs to.</summary>
    private async Task<Result<BookingListFilter, Error>> ScopeAsync(Id userId, UserRole role, CancellationToken cancellationToken)
    {
        if (role == UserRole.Customer)
            return new BookingListFilter(userId, null, null);

        if (role == UserRole.DealerOwner || role == UserRole.DealerEmployee)
        {
            // The resolver, not a raw lookup: a deactivated employee must not be handed the whole
            // booking book of the business that let them go.
            var member = await membership.ResolveAsync(userId, cancellationToken);
            if (member.IsFailure)
                return member.Error;

            return new BookingListFilter(null, member.Value.Dealer.Id, null);
        }

        // An administrator has no bookings "of their own"; the platform-wide list is an admin screen
        // with its own reader, not a special case of this one.
        return BookingErrors.NotAParty;
    }
}

/// <summary>One booking, for someone who is a party to it.</summary>
public sealed record GetBookingQuery(Id UserId, Id BookingId) : IQuery<Result<BookingDto, Error>>;

public sealed class GetBookingHandler(
    IBookingRepository bookings,
    IBookingReader reader,
    BookingPartyResolver parties,
    BookingPaymentAvailability paymentAvailability,
    IClock clock)
    : IRequestHandler<GetBookingQuery, Result<BookingDto, Error>>
{
    public async Task<Result<BookingDto, Error>> Handle(GetBookingQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var booking = await bookings.GetByIdAsync(request.BookingId, cancellationToken);
        if (booking is null)
            return BookingErrors.NotFound;

        // The resolver answers not_found for a stranger, so a real booking and a missing one look
        // identical from outside.
        var party = await parties.ResolveAsync(booking, request.UserId, cancellationToken);
        if (party.IsFailure)
            return party.Error;

        var context = await reader.ContextAsync(booking.Id, cancellationToken);

        // Only for the customer. The gallery and an administrator see the same booking without a
        // payment verdict, because neither has a Pay button and neither should be told whether the
        // customer currently has a checkout open.
        if (party.Value == BookingParty.Customer)
        {
            context = context with
            {
                Payment = await paymentAvailability.ForAsync(booking, cancellationToken)
            };
        }

        return BookingDto.From(booking, context, clock.UtcNow);
    }
}
