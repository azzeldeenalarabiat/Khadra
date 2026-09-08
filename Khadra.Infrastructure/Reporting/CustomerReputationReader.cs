using Khadra.Application.Reviews.ReadModels;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.Disputes;
using Khadra.Domain.Reviews;
using Khadra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Infrastructure.Reporting;

/// <summary>
/// What one gallery may know about a customer, composed from what the platform itself adjudicated.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here is a gallery's assertion. Every count comes from a booking the platform closed
/// against that booking's own frozen terms, and the attribution comes from
/// <c>PenaltyAssessment.AttributedTo</c> rather than from the status. That distinction is the whole
/// correctness of this file: a DELIVERY no-show is <c>Unattributed</c>, because the gallery was the
/// party who had to travel, and <c>ReportDealerNonDelivery</c> cancels a booking with
/// <c>CancelledBy = Customer</c> while attributing the penalty to the DEALER. Counting by status
/// would put both of those on the customer's record — the second one being, precisely, the customer
/// reporting that the GALLERY failed.
/// </para>
/// <para>
/// <b>The attribution is filtered in memory, and that is deliberate.</b> <c>Penalty</c> is a
/// <c>ToJson()</c> value object whose <c>AttributedTo</c> carries a value converter, and a predicate
/// reaching into that does not translate on every provider this project runs on — it would be a query
/// that works on Postgres and throws on the SQLite the persistence tests use, which is the worst of
/// both worlds. What IS pushed down is the expensive half: the customer id and the terminal statuses
/// are real indexed columns, so the database returns only this customer's finished bookings, and the
/// set is bounded by how many cars one person has rented. It is a handful of rows on a panel that
/// renders one booking.
/// </para>
/// </remarks>
internal sealed class CustomerReputationReader(KhadraDbContext context) : ICustomerReputationReader
{
    public async Task<CustomerReputation> GetAsync(
        Id customerId,
        Id viewingDealerId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var rating = await RatingAsync(customerId, now, cancellationToken);

        // Only the three terminal states that say something about the customer. Rejected and Expired
        // are absent on purpose: a gallery declining a request, or a platform expiring one nobody
        // paid, is not a fact about the person who asked.
        var outcomes = await context.Bookings
            .AsNoTracking()
            .Where(booking => booking.CustomerId == customerId)
            .Where(booking =>
                booking.Status == BookingStatus.Completed ||
                booking.Status == BookingStatus.NoShow ||
                booking.Status == BookingStatus.Cancelled)
            .Select(booking => new Outcome(booking.Id, booking.Status, booking.DealerId, booking.Penalty))
            .ToListAsync(cancellationToken);

        var completed = outcomes.Where(outcome => outcome.Status == BookingStatus.Completed).ToList();

        // Through the BOOKINGS, because a ticket carries no customer id -- only who opened it, which
        // is as often the gallery as the customer. The set is the same handful of ids already read.
        var bookingIds = outcomes.Select(outcome => outcome.BookingId).ToList();
        var disputes = bookingIds.Count == 0
            ? []
            : await context.DisputeTickets
                .AsNoTracking()
                .Where(ticket =>
                    bookingIds.Contains(ticket.BookingId) &&
                    ticket.Status == DisputeStatus.Resolved)
                .Select(ticket => ticket.Resolution)
                .ToListAsync(cancellationToken);

        var since = await context.Users
            .AsNoTracking()
            .Where(user => user.Id == customerId)
            .Select(user => (DateTimeOffset?)user.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return new CustomerReputation(
            rating,
            completed.Count,
            completed.Count(outcome => outcome.DealerId == viewingDealerId),
            outcomes.Count(outcome => outcome.Status == BookingStatus.NoShow && BlamesCustomer(outcome)),
            outcomes.Count(outcome => outcome.Status == BookingStatus.Cancelled && BlamesCustomer(outcome)),
            disputes.Count(WentAgainstCustomer),
            since ?? DateTimeOffset.MinValue);
    }

    /// <summary>
    /// The average and count of ratings other galleries left, aggregated in SQL.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two filters that the public gallery rating does NOT have, and both matter.
    /// </para>
    /// <para>
    /// <b>Revealed only.</b> A rating still inside its blind window is invisible to everyone but its
    /// author. Without this the whole window is decorative: a gallery would read the counterpart here
    /// and rate accordingly.
    /// </para>
    /// <para>
    /// <b>Hidden ratings do NOT count.</b> The opposite of the public direction, and the asymmetry is
    /// the point — see <c>ReviewDirection.HiddenScoreStillCounts</c>. There the score must survive
    /// moderation so a gallery cannot erase a bad rating by reporting the comment on it. Here there is
    /// no comment; the only thing an administrator can be hiding is the score, and the only reason to
    /// hide it is that it was wrong.
    /// </para>
    /// <para>
    /// Two queries rather than one <c>GroupBy(_ => 1)</c> projecting both aggregates: that shape
    /// translates on Postgres and not on SQLite, so it would ship a query that only ever fails in
    /// production. The same reasoning is recorded on <c>ReviewRepository.GetRatingSummaryAsync</c>.
    /// </para>
    /// </remarks>
    private async Task<RatingSummary> RatingAsync(
        Id customerId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var rated = context.Reviews
            .AsNoTracking()
            .Where(review =>
                review.SubjectId == customerId &&
                review.Direction == ReviewDirection.DealerRatesCustomer &&
                !review.IsHidden &&
                review.VisibleFrom <= now);

        var count = await rated.CountAsync(cancellationToken);
        if (count == 0)
            return RatingSummary.None;

        var average = await rated.AverageAsync(review => (decimal)review.Rating.Value, cancellationToken);
        // Rounded HERE, to the one decimal a rating is displayed to, because rounding it on the client
        // would be a screen deciding a number.
        return new RatingSummary(Math.Round(average, 1, MidpointRounding.AwayFromZero), count);
    }

    /// <summary>
    /// Whether this booking's own assessment put the fault on the customer.
    /// </summary>
    /// <remarks>
    /// A booking with no assessment blames nobody. So does a free cancellation, which records
    /// <c>PenaltyAssessment.None</c> and therefore <c>Unattributed</c> — a customer who cancelled
    /// inside their window did nothing another gallery needs to know about.
    /// </remarks>
    private static bool BlamesCustomer(Outcome outcome) =>
        outcome.Penalty is { } penalty && penalty.AttributedTo == BookingParty.Customer;

    /// <summary>
    /// Whether an administrator's resolution went against the customer.
    /// </summary>
    /// <remarks>
    /// "Not the whole deposit came back" rather than any named outcome, because
    /// <c>DisputeResolution</c> deliberately carries money instructions and not labels. A resolution
    /// that returned everything held is a ticket the customer WON, and counting it here would punish
    /// them for having been right.
    /// </remarks>
    private static bool WentAgainstCustomer(DisputeResolution? resolution) =>
        resolution is not null &&
        resolution.Deposit.RefundToCustomer.Amount < resolution.Deposit.DepositHeld.Amount;

    private sealed record Outcome(Id BookingId, BookingStatus Status, Id DealerId, PenaltyAssessment? Penalty);
}
