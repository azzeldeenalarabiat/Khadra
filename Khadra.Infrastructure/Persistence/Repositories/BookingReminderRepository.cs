using Khadra.Application.Bookings.Reminders;
using Khadra.Domain.Bookings;
using Khadra.Domain.Bookings.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Infrastructure.Persistence.Repositories;

internal sealed class BookingReminderRepository(KhadraDbContext context) : IBookingReminderRepository
{
    public async Task AddAsync(BookingReminder reminder, CancellationToken cancellationToken = default) =>
        await context.BookingReminders.AddAsync(reminder, cancellationToken);
}

/// <summary>
/// Which bookings are inside a reminder window and not yet reminded for this moment. Reads the booking
/// table directly (not through the settling repository): the pass has just settled, and a reminder is
/// a question about times already frozen on the row.
/// </summary>
internal sealed class ReminderCandidateReader(KhadraDbContext context) : IReminderCandidateReader
{
    public async Task<IReadOnlyList<ReminderCandidate>> ListDueAsync(
        ReminderKind kind,
        DateTimeOffset now,
        TimeSpan lead,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(kind);
        var until = now.Add(lead);
        var reminders = context.BookingReminders;
        var approved = BookingStatus.Approved;
        var confirmed = BookingStatus.Confirmed;
        var pickedUp = BookingStatus.PickedUp;

        // One projection per kind, each naming its own status and anchor. A booking in any other state
        // is never reminded: an expired request is not told to pay, a cancelled rental is not told to
        // collect, a returned car is not told to come back.
        var rows = kind == ReminderKind.Payment
            ? await context.Bookings
                .Where(b => b.Status == approved
                            && b.PaymentDeadline != null
                            && b.PaymentDeadline > now
                            && b.PaymentDeadline <= until)
                .Where(b => !reminders.Any(r => r.BookingId == b.Id && r.Kind == kind && r.AnchorAt == b.PaymentDeadline))
                .Select(b => new Row(b.Id, b.CustomerId, b.Reference.Value, b.PaymentDeadline!.Value))
                .ToListAsync(cancellationToken)
            : kind == ReminderKind.Pickup
                ? await context.Bookings
                    .Where(b => b.Status == confirmed
                                && b.Period.Start > now
                                && b.Period.Start <= until)
                    .Where(b => !reminders.Any(r => r.BookingId == b.Id && r.Kind == kind && r.AnchorAt == b.Period.Start))
                    .Select(b => new Row(b.Id, b.CustomerId, b.Reference.Value, b.Period.Start))
                    .ToListAsync(cancellationToken)
                : await context.Bookings
                    .Where(b => b.Status == pickedUp
                                && b.Period.End > now
                                && b.Period.End <= until)
                    .Where(b => !reminders.Any(r => r.BookingId == b.Id && r.Kind == kind && r.AnchorAt == b.Period.End))
                    .Select(b => new Row(b.Id, b.CustomerId, b.Reference.Value, b.Period.End))
                    .ToListAsync(cancellationToken);

        return [.. rows
            .OrderBy(row => row.Anchor)
            .Select(row => new ReminderCandidate(row.BookingId, row.CustomerId, row.Reference, row.Anchor))];
    }

    private sealed record Row(Khadra.Domain.Common.Id BookingId, Khadra.Domain.Common.Id CustomerId, string Reference, DateTimeOffset Anchor);
}
