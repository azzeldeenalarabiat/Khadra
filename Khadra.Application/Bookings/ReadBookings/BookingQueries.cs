using CSharpFunctionalExtensions;
using FluentValidation;
using Khadra.Application.Bookings.Dtos;
using Khadra.Application.Bookings.ReadModels;
using Khadra.Application.Common;
using Khadra.Domain.Bookings;
using Khadra.Domain.Bookings.Repositories;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Domain.Dealers.Repositories;
using Khadra.Domain.IdentityAccess;
using MediatR;

namespace Khadra.Application.Bookings.ReadBookings;

// The read-only bookings slice (spec 3.3 needs a booking to open a dispute FROM; spec 5 needs a
// customer to see what they booked). Deliberately no creation and no transitions: those belong to the
// Booking module proper, with the payment and overlap decisions it has to make. This slice only lets
// the two parties SEE the bookings the seeder -- and one day the real flow -- put there.

/// <summary>The signed-in person's bookings: theirs as a customer, or their dealership's as staff.</summary>
public sealed record ListMyBookingsQuery(Id UserId, UserRole Role, string? Status, PageRequest Page)
    : IQuery<Result<PagedResult<BookingListItem>, Error>>;

public sealed class ListMyBookingsQueryValidator : AbstractValidator<ListMyBookingsQuery>
{
    public ListMyBookingsQueryValidator()
    {
        RuleFor(query => query.Status)
            .Must(status => status is null ||
                            Enumeration.GetAll<BookingStatus>().Any(candidate =>
                                string.Equals(candidate.Name, status, StringComparison.OrdinalIgnoreCase)))
            .WithMessage("Unknown booking status.");
    }
}

public sealed class ListMyBookingsHandler(IBookingReader reader, IDealerRepository dealers)
    : IRequestHandler<ListMyBookingsQuery, Result<PagedResult<BookingListItem>, Error>>
{
    public async Task<Result<PagedResult<BookingListItem>, Error>> Handle(
        ListMyBookingsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        BookingListFilter filter;
        if (request.Role == UserRole.Customer)
        {
            filter = new BookingListFilter(request.UserId, null, request.Status);
        }
        else if (request.Role == UserRole.DealerOwner || request.Role == UserRole.DealerEmployee)
        {
            var dealer = await dealers.GetByOwnerUserIdAsync(request.UserId, cancellationToken)
                ?? await dealers.GetByStaffUserIdAsync(request.UserId, cancellationToken);
            if (dealer is null)
                return DealerErrors.NotRegistered;

            filter = new BookingListFilter(null, dealer.Id, request.Status);
        }
        else
        {
            // An administrator has no bookings "of their own"; the platform-wide list is an admin
            // screen with its own reader, not a special case of this one.
            return BookingErrors.NotAParty;
        }

        return await reader.ListAsync(filter, request.Page, cancellationToken);
    }
}

/// <summary>One booking, for someone who is a party to it.</summary>
public sealed record GetBookingQuery(Id UserId, Id BookingId) : IQuery<Result<BookingDto, Error>>;

public sealed class GetBookingHandler(
    IBookingRepository bookings,
    IBookingReader reader,
    BookingPartyResolver parties,
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
        return BookingDto.From(booking, context, clock.UtcNow);
    }
}
