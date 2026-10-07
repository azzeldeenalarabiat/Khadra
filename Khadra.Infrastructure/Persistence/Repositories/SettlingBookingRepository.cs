using Khadra.Application.Bookings;
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
/// <b>It settles and announces, in one breath</b> (Wave 4, checklist 234). A lapse settled here is announced through
/// <see cref="BookingExpiryAnnouncer"/> onto the same tracker, so its notifications persist exactly when the caller
/// saves the expiry and never when it does not. Before, a lapse settled on load and saved by some other command — a
/// capture landing seconds after the payment deadline, above all — expired the booking and told nobody.
/// </para>
/// <para>
/// <b>Only the single-booking loads.</b> The id lists are the SWEEP's, and pass straight through: the sweep then
/// loads each booking by id, through here, so its expiries are settled and announced by this one seam too.
/// </para>
/// <para>
/// <b>Concurrency.</b> Two requests may both load the same lapsed booking and both make the same
/// transition. They are identical, so whichever saves second loses on <c>xmin</c> and retries into a
/// context that reads it already settled — where <c>Settle</c> is a no-op. Nothing is applied twice
/// and nothing is lost.
/// </para>
/// </remarks>
internal sealed class SettlingBookingRepository(BookingRepository inner, IClock clock, BookingExpiryAnnouncer announcer)
    : IBookingRepository
{
    public async Task<Booking?> GetByIdAsync(Id id, CancellationToken cancellationToken = default) =>
        await SettledAsync(await inner.GetByIdAsync(id, cancellationToken), cancellationToken);

    public async Task<Booking?> GetByReferenceAsync(
        BookingReference reference,
        CancellationToken cancellationToken = default) =>
        await SettledAsync(await inner.GetByReferenceAsync(reference, cancellationToken), cancellationToken);

    private async Task<Booking?> SettledAsync(Booking? booking, CancellationToken cancellationToken)
    {
        if (booking is null)
            return null;

        var now = clock.UtcNow;
        if (BookingLapse.Settle(booking, now) is { } lapse)
            await announcer.AnnounceAsync(booking, lapse, now, cancellationToken);
        return booking;
    }

    // ---------------------------------------------------------------- straight through

    // The administrator's expiry, whose act is the settlement itself (pre-launch item 232). It expires the booking
    // in the administrator's name and announces it through the same BookingExpiryAnnouncer, so nothing is skipped.
    public Task<Booking?> GetByIdAsStoredAsync(Id id, CancellationToken cancellationToken = default) =>
        inner.GetByIdAsync(id, cancellationToken);

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

    public Task<IReadOnlyList<Id>> ListIdsDueForPaymentExpiryAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        inner.ListIdsDueForPaymentExpiryAsync(now, cancellationToken);

    public Task<IReadOnlyList<Id>> ListIdsDueForDecisionExpiryAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        inner.ListIdsDueForDecisionExpiryAsync(now, cancellationToken);

    public Task<IReadOnlyList<Id>> ListIdsDueForNoShowAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        inner.ListIdsDueForNoShowAsync(now, cancellationToken);

    public Task<IReadOnlyList<Id>> ListIdsDueForSettlementAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        inner.ListIdsDueForSettlementAsync(now, cancellationToken);

    public Task<IReadOnlyList<Id>> ListIdsDueForDepositReleaseAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        inner.ListIdsDueForDepositReleaseAsync(now, cancellationToken);

    public Task<IReadOnlyList<Booking>> ListStaleHoldsForVehicleAsync(
        Id vehicleId,
        DateRange candidatePeriod,
        TimeSpan turnaroundBuffer,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        inner.ListStaleHoldsForVehicleAsync(
            vehicleId, candidatePeriod, turnaroundBuffer, now, cancellationToken);
}
