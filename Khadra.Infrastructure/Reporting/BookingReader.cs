using Khadra.Application.Bookings.ReadModels;
using Khadra.Application.Common;
using Khadra.Application.Fleet.Dtos;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.Disputes;
using Khadra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Infrastructure.Reporting;

// Every join here is a LEFT join by id. Fleet, Dealers and Identity are other bounded contexts and a
// booking outlives all three: a delisted car, a deleted dealer, a closed account. An inner join would
// make such a booking vanish from the list, which for a financial record is the wrong kind of quiet.
internal sealed class BookingReader(KhadraDbContext context) : IBookingReader
{
    public async Task<PagedResult<BookingListItem>> ListAsync(
        BookingListFilter filter,
        PageRequest page,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(page);

        var open = DisputeStatus.Open;
        var underReview = DisputeStatus.UnderReview;

        var query = Scoped(filter);

        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            var status = Enumeration.GetAll<BookingStatus>()
                .SingleOrDefault(candidate =>
                    string.Equals(candidate.Name, filter.Status, StringComparison.OrdinalIgnoreCase));
            // Unknown status: nothing, not everything. The validator refuses it upstream anyway.
            if (status is null)
                return PagedResult.Empty<BookingListItem>(page.Page, page.PageSize);

            query = query.Where(booking => booking.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(filter.Tab))
        {
            if (!BookingTabs.IsKnown(filter.Tab))
                return PagedResult.Empty<BookingListItem>(page.Page, page.PageSize);

            query = ForTab(query, filter.Tab, open, underReview);
        }

        var total = await query.CountAsync(cancellationToken);
        if (total == 0)
            return PagedResult.Empty<BookingListItem>(page.Page, page.PageSize);

        var items = await query
            // Newest first: for a customer that is "the one I just made"; for a dealer it is the
            // queue of requests still waiting on them.
            .OrderByDescending(booking => booking.CreatedAt)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(booking => new BookingListItem(
                booking.Id.Value,
                booking.Reference.Value,
                booking.Status.Name,
                booking.Period.Start,
                booking.Period.End,
                booking.PickupMethod.Name,
                booking.Pricing.TotalPrice.Amount,
                booking.Pricing.TotalPrice.CurrencyCode,
                booking.CreatedAt,
                context.Vehicles
                    .Where(vehicle => vehicle.Id == booking.VehicleId)
                    .Select(vehicle => new VehicleLabel(
                        vehicle.Id.Value,
                        vehicle.Details.Make,
                        vehicle.Details.Model,
                        vehicle.Details.Year,
                        vehicle.Details.Color,
                        vehicle.PlateNumber.Value,
                        vehicle.Images
                            .Where(image => image.IsPrimary)
                            .Select(image => VehicleImageDto.PublicPath + "/" + image.StorageKey)
                            .FirstOrDefault()))
                    .FirstOrDefault(),
                context.Dealers
                    .Where(dealer => dealer.Id == booking.DealerId)
                    .Select(dealer => dealer.BusinessName.Value)
                    .FirstOrDefault() ?? "Dealer no longer on the platform",
                context.Users
                    .Where(user => user.Id == booking.CustomerId)
                    .Select(user => user.Name.Value)
                    .FirstOrDefault() ?? "Customer account closed",
                context.DisputeTickets.Any(ticket =>
                    ticket.BookingId == booking.Id &&
                    (ticket.Status == open || ticket.Status == underReview))))
            .ToListAsync(cancellationToken);

        return new PagedResult<BookingListItem>(items, page.Page, page.PageSize, total);
    }

    public async Task<IReadOnlyDictionary<string, int>> TabCountsAsync(
        BookingListFilter scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var open = DisputeStatus.Open;
        var underReview = DisputeStatus.UnderReview;
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        // One round trip per tab rather than a grouped query: eight small COUNTs against indexed
        // columns, and the code stays the same mapping the list uses, so a tab and its count can
        // never disagree about what belongs in it.
        foreach (var tab in BookingTabs.Names)
            counts[tab] = await ForTab(Scoped(scope), tab, open, underReview).CountAsync(cancellationToken);

        return counts;
    }

    /// <summary>The bookings this caller may see at all. A dealer never sees PendingPayment.</summary>
    private IQueryable<Booking> Scoped(BookingListFilter filter)
    {
        var query = context.Bookings.AsQueryable();

        if (filter.CustomerId is { } customerId)
            query = query.Where(booking => booking.CustomerId == customerId);

        if (filter.DealerId is { } dealerId)
        {
            var pendingPayment = BookingStatus.PendingPayment;
            query = query.Where(booking => booking.DealerId == dealerId && booking.Status != pendingPayment);
        }

        // One car's history (the vehicle detail screen): still inside the caller's own scope.
        if (filter.VehicleId is { } vehicleId)
        {
            var vehicle = Id.From(vehicleId);
            query = query.Where(booking => booking.VehicleId == vehicle);
        }

        return query;
    }

    private IQueryable<Booking> ForTab(IQueryable<Booking> query, string tab, DisputeStatus open, DisputeStatus underReview)
    {
        if (string.Equals(tab, BookingTabs.Disputed, StringComparison.OrdinalIgnoreCase))
        {
            return query.Where(booking => context.DisputeTickets.Any(ticket =>
                ticket.BookingId == booking.Id && (ticket.Status == open || ticket.Status == underReview)));
        }

        var statuses = BookingTabs.StatusesFor(tab);
        if (statuses is null)
            return query;

        // Listed out rather than Contains(): smart enums compare through a converter, and a plain
        // equality per member translates cleanly where a collection Contains does not.
        return statuses.Count switch
        {
            1 => query.Where(booking => booking.Status == statuses[0]),
            4 => query.Where(booking =>
                booking.Status == statuses[0] || booking.Status == statuses[1] ||
                booking.Status == statuses[2] || booking.Status == statuses[3]),
            _ => query,
        };
    }

    public async Task<BookingContext> ContextAsync(Id bookingId, CancellationToken cancellationToken = default)
    {
        var open = DisputeStatus.Open;
        var underReview = DisputeStatus.UnderReview;

        var found = await context.Bookings
            .Where(booking => booking.Id == bookingId)
            .Select(booking => new BookingContext(
                context.Vehicles
                    .Where(vehicle => vehicle.Id == booking.VehicleId)
                    .Select(vehicle => new VehicleLabel(
                        vehicle.Id.Value,
                        vehicle.Details.Make,
                        vehicle.Details.Model,
                        vehicle.Details.Year,
                        vehicle.Details.Color,
                        vehicle.PlateNumber.Value,
                        vehicle.Images
                            .Where(image => image.IsPrimary)
                            .Select(image => VehicleImageDto.PublicPath + "/" + image.StorageKey)
                            .FirstOrDefault()))
                    .FirstOrDefault(),
                context.Dealers
                    .Where(dealer => dealer.Id == booking.DealerId)
                    .Select(dealer => dealer.BusinessName.Value)
                    .FirstOrDefault() ?? "Dealer no longer on the platform",
                context.Users
                    .Where(user => user.Id == booking.CustomerId)
                    .Select(user => user.Name.Value)
                    .FirstOrDefault() ?? "Customer account closed",
                context.DisputeTickets
                    .Where(ticket =>
                        ticket.BookingId == booking.Id &&
                        (ticket.Status == open || ticket.Status == underReview))
                    .Select(ticket => (Guid?)ticket.Id.Value)
                    .FirstOrDefault()))
            .SingleOrDefaultAsync(cancellationToken);

        // The caller has already loaded the aggregate, so a miss here is a race with a delete that
        // cannot happen (bookings are never deleted). Empty labels keep the contract total anyway.
        return found ?? new BookingContext(null, string.Empty, string.Empty, null);
    }
}
