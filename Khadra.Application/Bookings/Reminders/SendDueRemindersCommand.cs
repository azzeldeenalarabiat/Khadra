using CSharpFunctionalExtensions;
using Khadra.Application.Bookings.ReadModels;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.Notifications;
using Khadra.Domain.Bookings;
using Khadra.Domain.Bookings.Repositories;
using Khadra.Domain.Common;
using Khadra.Domain.Notifications;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Khadra.Application.Bookings.Reminders;

/// <summary>
/// How long before each moment a customer is reminded. Configuration (<c>Reminders</c>), set by the
/// owner: one hour before pickup, one hour before return, thirty minutes before the deposit deadline.
/// </summary>
/// <remarks>
/// Not frozen on the booking, deliberately. These decide when a MESSAGE goes out, not what the customer
/// agreed to; the moment a reminder is about (the deadline, the start, the end) is the booking's own
/// frozen value, and that is what the message states.
/// </remarks>
public interface IReminderSettings
{
    TimeSpan PaymentLead { get; }

    TimeSpan PickupLead { get; }

    TimeSpan ReturnLead { get; }
}

/// <summary>One booking inside a reminder window that has not been reminded about this moment yet.</summary>
public sealed record ReminderCandidate(Id BookingId, Id CustomerId, string Reference, DateTimeOffset AnchorAt);

/// <summary>Finds the bookings a reminder is owed to. A projection, never a loaded aggregate.</summary>
public interface IReminderCandidateReader
{
    /// <summary>
    /// Bookings in the state the kind needs whose anchor falls in (<paramref name="now"/>,
    /// <paramref name="now"/> + <paramref name="lead"/>] and that have no reminder of this kind for
    /// this anchor. Never a booking whose moment has already passed: a reminder after the fact is not
    /// a reminder.
    /// </summary>
    Task<IReadOnlyList<ReminderCandidate>> ListDueAsync(
        ReminderKind kind,
        DateTimeOffset now,
        TimeSpan lead,
        CancellationToken cancellationToken = default);
}

/// <param name="Payment">Deposit reminders sent this pass.</param>
/// <param name="Pickup">Pickup reminders sent this pass.</param>
/// <param name="Return">Return reminders sent this pass.</param>
public sealed record ReminderReport(int Payment, int Pickup, int Return);

/// <summary>Sends every reminder that has come due. Run inside the settlement pass, after settlement.</summary>
public sealed record SendDueRemindersCommand : ICommand<Result<ReminderReport, Error>>;

/// <remarks>
/// <para>
/// <b>Exactly once per booking and moment.</b> The reminder record and the notification it raises are
/// saved together, one booking at a time, and the record's unique key refuses a duplicate. The
/// notification then reaches the phone and the inbox through the outbox, with its retries. Nothing
/// here sends anything directly.
/// </para>
/// <para>
/// <b>After settlement, in the same pass.</b> A booking whose payment window has just closed is expired
/// first, so it is never told to pay for something that is already gone.
/// </para>
/// <para>
/// <b>No payment reminder while payments are None.</b> A push telling a customer to pay, when every
/// checkout is refused, is the promise <c>YourDepositDue</c> was never added for. The mode is the
/// provider's own answer; reading it changes nothing about payments.
/// </para>
/// <para>
/// <b>Late is allowed, after is not.</b> A process that was down through a reminder's moment sends it
/// as soon as it is back — provided the pickup, return or deadline has not already arrived.
/// </para>
/// </remarks>
public sealed partial class SendDueRemindersHandler(
    IReminderCandidateReader candidates,
    IBookingReminderRepository reminders,
    IBookingReader bookings,
    DealerTeamNotifier team,
    IReminderSettings settings,
    IPaymentProvider payments,
    IUnitOfWork unitOfWork,
    IClock clock,
    ILogger<SendDueRemindersHandler> logger)
    : IRequestHandler<SendDueRemindersCommand, Result<ReminderReport, Error>>
{
    public async Task<Result<ReminderReport, Error>> Handle(SendDueRemindersCommand request, CancellationToken cancellationToken)
    {
        var payment = payments.Mode == PaymentMode.None
            ? 0
            : await SendAsync(ReminderKind.Payment, settings.PaymentLead, NotificationKind.YourPaymentReminder, cancellationToken);
        if (payment < 0)
            return new ReminderReport(0, 0, 0);

        var pickup = await SendAsync(ReminderKind.Pickup, settings.PickupLead, NotificationKind.YourPickupReminder, cancellationToken);
        if (pickup < 0)
            return new ReminderReport(payment, 0, 0);

        var @return = await SendAsync(ReminderKind.Return, settings.ReturnLead, NotificationKind.YourReturnReminder, cancellationToken);
        return new ReminderReport(payment, pickup, Math.Max(@return, 0));
    }

    /// <returns>How many were sent, or -1 when the pass must stop (another process is sending the same ones).</returns>
    private async Task<int> SendAsync(
        ReminderKind kind,
        TimeSpan lead,
        NotificationKind notificationKind,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var due = await candidates.ListDueAsync(kind, now, lead, cancellationToken);
        var sent = 0;

        foreach (var candidate in due)
        {
            var context = await bookings.ContextAsync(candidate.BookingId, cancellationToken);

            await reminders.AddAsync(BookingReminder.Record(candidate.BookingId, kind, candidate.AnchorAt, now), cancellationToken);
            await team.NotifyCustomerAsync(
                candidate.CustomerId,
                context.DealerName,
                notificationKind,
                now,
                candidate.BookingId,
                candidate.Reference,
                candidate.AnchorAt);

            try
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
                sent++;
            }
            catch (UniqueConstraintConflictException)
            {
                // Another process sent this one a moment ago; the index kept it to one. The rejected
                // rows are still tracked on this unit of work and would be written again by the next
                // save, so the pass stops here — the next pass, a minute away, picks up anything left,
                // and the other process is almost certainly already doing so.
                LogRaced(logger, kind.Name);
                return -1;
            }
        }

        if (sent > 0)
            LogSent(logger, sent, kind.Name);
        return sent;
    }

    [LoggerMessage(2330, LogLevel.Information, "Sent {Count} {Kind} reminder(s).")]
    private static partial void LogSent(ILogger logger, int count, string kind);

    [LoggerMessage(2331, LogLevel.Information,
        "A {Kind} reminder was already sent by another process; this pass stops and the next continues.")]
    private static partial void LogRaced(ILogger logger, string kind);
}
