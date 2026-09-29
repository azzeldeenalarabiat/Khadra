using Khadra.Application.Payables.ReadModels;
using Khadra.Domain.Bookings;
using Khadra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Infrastructure.Reporting;

/// <summary>
/// What the payables pass looks at (payments Phase 8), found cheaply in SQL. The durable facts ARE the queue: a paid
/// booking that ended with no payable row is owed one, so nothing records that a payable is due and nothing can be
/// lost. Every decision — final or not, recorded or held — is the calculator's, in the step.
/// </summary>
internal sealed class PayableWorkReader(KhadraDbContext context) : IPayableWorkReader
{
    public async Task<PayableWork> ListAsync(
        DateTimeOffset now,
        DateTimeOffset completedBefore,
        DateTimeOffset cancelledBefore,
        int maxBookings,
        int verifyOffset,
        int maxVerifications,
        CancellationToken cancellationToken = default)
    {
        var completed = BookingStatus.Completed;
        var cancelled = BookingStatus.Cancelled;
        var noShow = BookingStatus.NoShow;

        // Paid, ended in a status that can owe an office, past the margin — and a cancellation or a no-show past
        // today's window too — with no payable, and not held back until later. Completed rentals first.
        var bookings = await context.Bookings
            .AsNoTracking()
            .Where(booking =>
                booking.DepositPaymentId != null &&
                booking.FinishedAt != null &&
                ((booking.Status == completed && booking.FinishedAt <= completedBefore) ||
                 ((booking.Status == cancelled || booking.Status == noShow) && booking.FinishedAt <= cancelledBefore)) &&
                !context.OfficePayables.Any(payable => payable.BookingId == booking.Id) &&
                !context.OfficePayableHolds.Any(hold =>
                    hold.BookingId == booking.Id &&
                    hold.ReleasedAt == null &&
                    hold.PayableId == null &&
                    hold.NextCheckAt > now))
            .OrderBy(booking => booking.Status == completed ? 0 : 1)
            .ThenBy(booking => booking.FinishedAt)
            .ThenBy(booking => booking.Id)
            .Select(booking => booking.Id)
            .Take(maxBookings)
            .ToListAsync(cancellationToken);

        // One page of the open payables that carry money, in a stable order, a page per tick: every one is checked
        // again in turn, however many there are. A payable settled in between shifts the pages, and is simply looked
        // at on the next round.
        var payables = await context.OfficePayables
            .AsNoTracking()
            .Where(payable => payable.SettlementId == null && payable.Net != 0m)
            .OrderBy(payable => payable.RecordedAt)
            .ThenBy(payable => payable.Id)
            .Select(payable => payable.Id)
            .Skip(verifyOffset)
            .Take(maxVerifications)
            .ToListAsync(cancellationToken);

        return new PayableWork(bookings, payables, payables.Count < maxVerifications ? 0 : verifyOffset + payables.Count);
    }
}
