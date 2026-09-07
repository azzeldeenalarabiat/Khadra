using CSharpFunctionalExtensions;
using FluentValidation;
using Khadra.Application.Bookings.Dtos;
using Khadra.Application.Bookings.ReadModels;
using Khadra.Application.Common;
using Khadra.Domain.Bookings;
using Khadra.Domain.Bookings.Repositories;
using Khadra.Domain.Common;
using MediatR;

namespace Khadra.Application.Bookings.AdminBookings;

// The platform's own view of every booking, across all dealers (spec 3.3). It reads through the same
// IBookingReader both parties use rather than a second reader of its own: one projection means a
// booking cannot look different to an admin than it does to the people it belongs to. It now asks
// for nothing the parties do not: the reader used to hide unpaid requests from a dealer and needed
// an opt-out for the admin, and since 2026-09-07 it hides nothing from anyone.

/// <summary>Every booking on the platform, filtered the way an admin working it would.</summary>
public sealed record ListAllBookingsQuery(
    string? Status,
    string? Tab,
    Guid? DealerId,
    Guid? CustomerId,
    string? Reference,
    PageRequest Page)
    : IQuery<Result<PagedResult<BookingListItem>, Error>>;

/// <summary>How many bookings sit behind each tab, platform-wide and under the same filters.</summary>
public sealed record GetAllBookingTabCountsQuery(Guid? DealerId, Guid? CustomerId)
    : IQuery<Result<IReadOnlyDictionary<string, int>, Error>>;

public sealed class ListAllBookingsQueryValidator : AbstractValidator<ListAllBookingsQuery>
{
    public ListAllBookingsQueryValidator()
    {
        RuleFor(query => query.Status)
            .Must(status => status is null ||
                            Enumeration.GetAll<BookingStatus>().Any(candidate =>
                                string.Equals(candidate.Name, status, StringComparison.OrdinalIgnoreCase)))
            .WithMessage("Unknown booking status.");
        RuleFor(query => query.Tab)
            .Must(BookingTabs.IsKnown)
            .WithMessage("Unknown bookings tab.");
        RuleFor(query => query.Reference)
            .MaximumLength(32)
            .WithMessage("A booking reference is at most 32 characters.");
    }
}

public sealed class AdminBookingQueryHandlers(IBookingReader reader) :
    IRequestHandler<ListAllBookingsQuery, Result<PagedResult<BookingListItem>, Error>>,
    IRequestHandler<GetAllBookingTabCountsQuery, Result<IReadOnlyDictionary<string, int>, Error>>
{
    public async Task<Result<PagedResult<BookingListItem>, Error>> Handle(
        ListAllBookingsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var page = await reader.ListAsync(FilterFor(request), request.Page, cancellationToken);
        return page;
    }

    public async Task<Result<IReadOnlyDictionary<string, int>, Error>> Handle(
        GetAllBookingTabCountsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var counts = await reader.TabCountsAsync(
            Scope(request.DealerId, request.CustomerId),
            cancellationToken);
        return Result.Success<IReadOnlyDictionary<string, int>, Error>(counts);
    }

    private static BookingListFilter FilterFor(ListAllBookingsQuery request) =>
        Scope(request.DealerId, request.CustomerId) with
        {
            Status = request.Status,
            Tab = request.Tab,
            Reference = request.Reference,
        };

    /// <summary>
    /// The Admin's scope is the whole platform, narrowed only by an explicit filter.
    /// </summary>
    private static BookingListFilter Scope(Guid? dealerId, Guid? customerId) =>
        new(
            CustomerId: customerId is { } customer ? Id.From(customer) : null,
            DealerId: dealerId is { } dealer ? Id.From(dealer) : null,
            Status: null,
            Reference: null);
}

/// <summary>One booking as the platform sees it, with no party check.</summary>
public sealed record GetAnyBookingQuery(Id BookingId) : IQuery<Result<BookingDto, Error>>;

public sealed class GetAnyBookingHandler(
    IBookingRepository bookings,
    IBookingReader reader,
    IClock clock)
    : IRequestHandler<GetAnyBookingQuery, Result<BookingDto, Error>>
{
    public async Task<Result<BookingDto, Error>> Handle(GetAnyBookingQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var booking = await bookings.GetByIdAsync(request.BookingId, cancellationToken);
        if (booking is null)
            return BookingErrors.NotFound;

        // No BookingPartyResolver: an administrator is not a party to the booking and does not need
        // to be one. The controller's Admin policy is the whole of the authorisation here.
        var context = await reader.ContextAsync(booking.Id, cancellationToken);
        return BookingDto.From(booking, context, clock.UtcNow);
    }
}
