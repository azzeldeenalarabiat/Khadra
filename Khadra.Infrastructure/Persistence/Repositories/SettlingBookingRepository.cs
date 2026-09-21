using Khadra.Application.Common;
using Khadra.Domain.Bookings;
using Khadra.Domain.Bookings.Repositories;
using Khadra.Domain.Common;

namespace Khadra.Infrastructure.Persistence.Repositories;

/// <summary>
/// Loads a booking already settled against the clock, so no handler has to remember to.
/// </summary>
/// <remarks>
/// <para>
/// <b>The seam.</b> Seventeen call sites load a single booking and act on it, and asking each of
/// them to call <c>BookingLapse.Settle</c> first is asking to be right seventeen times and then
/// wrong on the eighteenth. This is the one place they all pass through.
/// </para>
/// <para>
/// <b>It settles; it does not save.</b> That is what keeps a read from becoming a write. A query
/// handler gets an aggregate telling the truth and writes nothing, because it never calls
/// <c>SaveChangesAsync</c>; a command handler persists the settlement along with whatever it came to
/// do, in its own transaction. So the stored row may read <c>Approved</c> for a while after its
/// window closed — deliberately — while nothing anywhere treats it as live. The settlement sweep
/// normalises the rows eventually, as an optimisation rather than as the source of truth, which
/// matters because the API sleeps on Render's free tier and may not run for hours.
/// </para>
/// <para>
/// <b>Only the single-booking loads.</b> The <c>ListDueFor*</c> queries are the SWEEP's, and it
/// calls <c>ExpireUnpaid</c> / <c>ExpireUnanswered</c> itself; settling them here would leave the
/// sweep's own call refusing a booking this had already transitioned, and it would log that as a
/// failure. They pass straight through.
/// </para>
/// <para>
/// <b>Concurrency.</b> Two requests may both load the same lapsed booking and both make the same
/// transition. They are identical, so whichever saves second loses on <c>xmin</c> and retries into a
/// context that reads it already settled — where <c>Settle</c> is a no-op. Nothing is applied twice
/// and nothing is lost.
/// </para>
/// </remarks>
internal sealed class SettlingBookingRepository(BookingRepository inner, IClock clock) : IBookingRepository
{
    public async Task<Booking?> GetByIdAsync(Id id, CancellationToken cancellationToken = default) =>
        Settled(await inner.GetByIdAsync(id, cancellationToken));

    public async Task<Booking?> GetByReferenceAsync(
        BookingReference reference,
        CancellationToken cancellationToken = default) =>
        Settled(await inner.GetByReferenceAsync(reference, cancellationToken));

    private Booking? Settled(Booking? booking)
    {
        if (booking is not null) BookingLapse.Settle(booking, clock.UtcNow);
        return booking;
    }

    // ---------------------------------------------------------------- straight through

    public Task AddAsync(Booking booking, CancellationToken cancellationToken = default) =>
        inner.AddAsync(booking, cancellationToken);

    public Task<bool> HasOverlappingBookingAsync(
        Id vehicleId,
        DateRange period,
        TimeSpan turnaroundBuffer,
        DateTimeOffset now,
        Id? excludingBookingId,
        CancellationToken cancellationToken = default) =>
        inner.HasOverlappingBookingAsync(
            vehicleId, period, turnaroundBuffer, now, excludingBookingId, cancellationToken);

    public Task<IReadOnlyList<Booking>> ListDueForPaymentExpiryAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        inner.ListDueForPaymentExpiryAsync(now, cancellationToken);

    public Task<IReadOnlyList<Booking>> ListDueForDecisionExpiryAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        inner.ListDueForDecisionExpiryAsync(now, cancellationToken);

    public Task<IReadOnlyList<Booking>> ListDueForNoShowAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        inner.ListDueForNoShowAsync(now, cancellationToken);

    public Task<IReadOnlyList<Booking>> ListDueForSettlementAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        inner.ListDueForSettlementAsync(now, cancellationToken);

    public Task<IReadOnlyList<Booking>> ListStaleHoldsForVehicleAsync(
        Id vehicleId,
        DateRange candidatePeriod,
        TimeSpan turnaroundBuffer,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        inner.ListStaleHoldsForVehicleAsync(
            vehicleId, candidatePeriod, turnaroundBuffer, now, cancellationToken);
}
