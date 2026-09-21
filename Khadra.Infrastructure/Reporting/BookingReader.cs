using Khadra.Application.Bookings.ReadModels;
using Khadra.Application.Common;
using Khadra.Application.Fleet.Dtos;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.Disputes;
using Khadra.Domain.Reviews;
using Khadra.Infrastructure.Persistence;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Infrastructure.Reporting;

// Every join here is a LEFT join by id. Fleet, Dealers and Identity are other bounded contexts and a
// booking outlives all three: a delisted car, a deleted dealer, a closed account. An inner join would
// make such a booking vanish from the list, which for a financial record is the wrong kind of quiet.
internal sealed class BookingReader(KhadraDbContext context) : IBookingReader
{
    // English stand-ins for a party that no longer resolves, kept ONLY because shipped customer apps
    // print the name as it arrives. A client that words the case reads the DealerRemoved /
    // CustomerAccountClosed flag instead and never shows these.
    private const string RemovedDealerName = "Dealer no longer on the platform";
    private const string ClosedCustomerName = "Customer account closed";

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

        var rows = await query
            // Newest first: for a customer that is "the one I just made"; for a dealer it is the
            // queue of requests still waiting on them.
            .OrderByDescending(booking => booking.CreatedAt)
            // CreatedAt alone is not unique -- the seeder alone writes several in a second -- and a
            // non-total order lets a page boundary drop a booking or show it on two pages.
            .ThenByDescending(booking => booking.Id)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(ToListRow())
            .ToListAsync(cancellationToken);

        return new PagedResult<BookingListItem>(
            rows.Select(row => row.ToItem()).ToList(), page.Page, page.PageSize, total);
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

    /// <summary>
    /// One booking as a list row, defined once.
    /// </summary>
    /// <remarks>
    /// Two callers build this shape — the paged list and the "next booking" the landing surface
    /// shows — and a second copy would drift the first time a field was added to one of them. Every
    /// correlation is a LEFT join by id: Fleet, Dealers and Identity are other bounded contexts, and
    /// a booking outlives all three.
    ///
    /// It is a METHOD returning the expression rather than a static field because it closes over the
    /// DbContext, and EF inlines a locally-bound expression into the query the same way it inlines a
    /// literal one.
    ///
    /// It projects a <see cref="ListRow"/> rather than the list item itself so that each party's name
    /// is read ONCE, arriving as null when it no longer resolves, and both the flag and the stand-in
    /// are taken from that one read after materialisation. Deriving the flag in SQL would ask the
    /// same question twice, in two subqueries that agree only by construction.
    /// </remarks>
    private Expression<Func<Booking, ListRow>> ToListRow()
    {
        var open = DisputeStatus.Open;
        var underReview = DisputeStatus.UnderReview;

        return booking => new ListRow(
            booking.Id.Value,
            booking.Reference.Value,
            booking.Status.Name,
            booking.Period.Start,
            booking.Period.End,
            booking.Pricing.Days,
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
                .FirstOrDefault(),
            context.Users
                .Where(user => user.Id == booking.CustomerId)
                .Select(user => user.Name.Value)
                .FirstOrDefault(),
            context.DisputeTickets.Any(ticket =>
                ticket.BookingId == booking.Id &&
                (ticket.Status == open || ticket.Status == underReview)),
            booking.DealerId.Value,
            booking.CustomerId.Value);
    }

    /// <summary>A list row as it leaves the database: each party's name is null when it did not resolve.</summary>
    private sealed record ListRow(
        Guid BookingId,
        string Reference,
        string Status,
        DateTimeOffset PeriodStart,
        DateTimeOffset PeriodEnd,
        int Days,
        string PickupMethod,
        decimal TotalPrice,
        string Currency,
        DateTimeOffset CreatedAt,
        VehicleLabel? Vehicle,
        string? DealerName,
        string? CustomerName,
        bool HasLiveDispute,
        Guid DealerId,
        Guid CustomerId)
    {
        public BookingListItem ToItem() => new(
            BookingId,
            Reference,
            Status,
            PeriodStart,
            PeriodEnd,
            Days,
            PickupMethod,
            TotalPrice,
            Currency,
            CreatedAt,
            Vehicle,
            DealerName ?? RemovedDealerName,
            DealerRemoved: DealerName is null,
            CustomerName ?? ClosedCustomerName,
            CustomerAccountClosed: CustomerName is null,
            HasLiveDispute,
            DealerId,
            CustomerId);
    }

    /// <summary>The bookings this caller may see at all: their own, and nothing else filtered out.</summary>
    private IQueryable<Booking> Scoped(BookingListFilter filter)
    {
        var query = context.Bookings.AsQueryable();

        if (filter.CustomerId is { } customerId)
            query = query.Where(booking => booking.CustomerId == customerId);

        // A dealer used to be shielded from unpaid requests, because under the old order a request
        // without a deposit was not yet a request at all (spec 5.3). Since 2026-09-07 it is precisely
        // the thing they must answer, and hiding it would leave the queue empty while cars sat held.
        // Nothing is filtered out of a dealer's list any more.
        if (filter.DealerId is { } dealerId)
            query = query.Where(booking => booking.DealerId == dealerId);

        if (!string.IsNullOrWhiteSpace(filter.Reference))
        {
            // Matched in full through the value-object converter, which EF translates. ILIKE across
            // that converter does not translate, and a reference is quoted whole by whoever is
            // looking for it, so a prefix search would buy nothing.
            var reference = BookingReference.Create(filter.Reference.Trim().ToUpperInvariant());
            query = reference.IsFailure
                ? query.Where(_ => false)
                : query.Where(booking => booking.Reference == reference.Value);
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
        if (statuses.Count == 0)
            throw new ArgumentException("A tab must name at least one status.", nameof(tab));

        // One IN over the converted column, for a tab of any size. Two earlier shapes were wrong:
        // a switch on 1 and 4 that fell through to the UNFILTERED query for every other count, so a
        // two-status tab quietly listed the whole scope; and a Concat of one query per status, which
        // EF cannot translate at all once the projection reaches into a JSON-mapped value object.
        //
        // Materialised into a List first: EF translates Contains over a local list, not over an
        // IReadOnlyList it cannot recognise as a parameter.
        var wanted = statuses.ToList();
        return query.Where(booking => wanted.Contains(booking.Status));
    }

    /// <summary>
    /// The customer's most pressing live booking.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Four candidate queries in a fixed order rather than one clever sort, because the ranking is a
    /// JUDGEMENT and reads as one this way. A deposit due before a deadline that will not wait beats
    /// a car already out, which beats one paid for and not yet collected, which beats a request the
    /// gallery has not answered. Only the first that finds anything is returned.
    /// </para>
    /// <para>
    /// Four round trips at worst, and in practice one: the common answer is the first query finding
    /// nothing and the fourth finding nothing either, on a customer-id index. It is a fixed small
    /// number that does not grow with the customer's history, which the `ListAsync` shape would not
    /// have given — the "nearest pickup" is not a column, and ordering by it means reading rows the
    /// screen throws away.
    /// </para>
    /// <para>
    /// **`Approved` and `Confirmed` are split here where the tabs join them.** The tab is right for a
    /// list — both are answered and neither collected — but this is the one place the difference is
    /// the whole point: one of them is owed money within hours and the other is not.
    /// </para>
    /// </remarks>
    public async Task<NextBooking?> NextForCustomerAsync(
        Id customerId,
        CancellationToken cancellationToken = default)
    {
        // Soonest first in every case. A customer with two upcoming rentals wants the one that
        // starts on Thursday, not the one they happened to book last.
        async Task<NextBooking?> FirstAsync(BookingStatus status, string reason, bool byEnd = false)
        {
            var query = context.Bookings
                .Where(booking => booking.CustomerId == customerId && booking.Status == status);

            query = byEnd
                ? query.OrderBy(booking => booking.Period.End).ThenBy(booking => booking.Id)
                : query.OrderBy(booking => booking.Period.Start).ThenBy(booking => booking.Id);

            var found = await query.Take(1).Select(ToListRow()).FirstOrDefaultAsync(cancellationToken);
            return found is null ? null : new NextBooking(found.ToItem(), reason);
        }

        return await FirstAsync(BookingStatus.Approved, NextBookingReason.AwaitingPayment)
            // The car is OUT: what is ahead is the return, so that is what orders it.
            ?? await FirstAsync(BookingStatus.PickedUp, NextBookingReason.InProgress, byEnd: true)
            ?? await FirstAsync(BookingStatus.Confirmed, NextBookingReason.Upcoming)
            ?? await FirstAsync(BookingStatus.Requested, NextBookingReason.AwaitingDecision);
    }

    public async Task<BookingContext> ContextAsync(Id bookingId, CancellationToken cancellationToken = default)
    {
        var open = DisputeStatus.Open;
        var underReview = DisputeStatus.UnderReview;
        var customerRatesDealer = ReviewDirection.CustomerRatesDealer;

        // Each party's name is read once, as null when it no longer resolves, and the flag and the
        // stand-in are both taken from that read -- the same shape the list row uses.
        var found = await context.Bookings
            .Where(booking => booking.Id == bookingId)
            .Select(booking => new ContextRow(
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
                // ONE subquery, two columns. The dealership must be read exactly once, or the name
                // and the "no longer resolves" flag could disagree; the city rides in the same
                // SELECT rather than opening a second lookup for it.
                context.Dealers
                    .Where(dealer => dealer.Id == booking.DealerId)
                    .Select(dealer => new DealerLabel(
                        dealer.BusinessName.Value,
                        dealer.CityId == null ? null : (Guid?)dealer.CityId.Value))
                    .FirstOrDefault(),
                context.Users
                    .Where(user => user.Id == booking.CustomerId)
                    .Select(user => user.Name.Value)
                    .FirstOrDefault(),
                context.DisputeTickets
                    .Where(ticket =>
                        ticket.BookingId == booking.Id &&
                        (ticket.Status == open || ticket.Status == underReview))
                    .Select(ticket => (Guid?)ticket.Id.Value)
                    .FirstOrDefault(),
                context.Reviews
                    .Where(review =>
                        review.BookingId == booking.Id &&
                        review.Direction == customerRatesDealer)
                    .Select(review => (Guid?)review.Id.Value)
                    .FirstOrDefault()))
            .SingleOrDefaultAsync(cancellationToken);

        // The caller has already loaded the aggregate, so a miss here is a race with a delete that
        // cannot happen (bookings are never deleted). Empty labels keep the contract total anyway, and
        // no flag is raised: nothing was looked up, so nothing failed to resolve.
        if (found is null)
            return new BookingContext(null, string.Empty, DealerRemoved: false, null, string.Empty, CustomerAccountClosed: false, null, null);

        return new BookingContext(
            found.Vehicle,
            found.Dealer?.Name ?? RemovedDealerName,
            DealerRemoved: found.Dealer is null,
            found.Dealer?.CityId,
            found.CustomerName ?? ClosedCustomerName,
            CustomerAccountClosed: found.CustomerName is null,
            found.LiveDisputeId,
            found.MyReviewId);
    }

    /// <summary>A booking's context as it leaves the database: each party's name is null when it did not resolve.</summary>
    /// <summary>The dealership as the query found it, or null when it no longer resolves.</summary>
    private sealed record DealerLabel(string Name, Guid? CityId);

    private sealed record ContextRow(
        VehicleLabel? Vehicle,
        DealerLabel? Dealer,
        string? CustomerName,
        Guid? LiveDisputeId,
        Guid? MyReviewId);
}
