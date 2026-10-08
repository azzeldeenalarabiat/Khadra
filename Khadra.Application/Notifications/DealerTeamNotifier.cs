using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Domain.IdentityAccess.Repositories;
using Khadra.Domain.Notifications;
using Khadra.Domain.Notifications.Repositories;

namespace Khadra.Application.Notifications;

/// <summary>
/// Tells a dealership's people what just happened to their booking book.
///
/// Most of it is what one of THEM did: a dealership is a team, and when one member of staff answers a
/// request or hands a car over, that is news to the owner and to every colleague who shares the
/// booking book with them. Spec 4.2 asks for exactly this accountability, and until this existed it
/// lived only on the booking's own history where nobody was looking.
///
/// Since 2026-09-07 things arrive from outside the dealership as well. What a customer did — asking for
/// a car, cancelling, paying, reporting a car not handed over, opening a dispute — goes through
/// NotifyTeamOfCustomerActionAsync, which names nobody. What the platform did — an expiry, a no-show, a
/// completion, an administrator's decision, a settlement — goes through NotifyTeamFromPlatformAsync and
/// NotifyReportReadersFromPlatformAsync, which name Khadra (Fix & Polish Wave 3, C5).
///
/// The actor is never notified of their own action. They were there.
///
/// Nothing is saved here: rows are STAGED on the caller's DbContext and committed by the caller's own
/// SaveChangesAsync, so the notification and the action it describes land in one transaction or not
/// at all. That is why this takes an <see cref="INotifier"/> and not a repository.
/// </summary>
public sealed class DealerTeamNotifier(INotifier notifier, IUserRepository users)
{
    /// <summary>
    /// Everyone at <paramref name="dealer"/> except the actor, told that something happened.
    ///
    /// The actor's NAME is snapshotted onto each row, following <c>AuditEntry</c>: "Ahmad approved
    /// KR-1042" has to still read correctly after Ahmad is renamed or leaves. Only the actor's name
    /// is stored — never the customer's, because this table is not deleted from.
    /// </summary>
    public async Task NotifyTeamAsync(
        Dealer dealer,
        Id actorUserId,
        NotificationKind kind,
        DateTimeOffset now,
        Id? subjectId = null,
        string? subjectReference = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dealer);
        ArgumentNullException.ThrowIfNull(kind);

        var recipients = Recipients(dealer, exceptUserId: actorUserId);
        if (recipients.Count == 0)
            return;

        var actorName = await NameOfAsync(actorUserId, cancellationToken);
        notifier.RaiseMany(recipients.Select(recipient =>
            ByPerson(recipient, kind, actorName, now, subjectId, subjectReference, actorUserId)));
    }

    /// <summary>
    /// Everyone at <paramref name="dealer"/>, told that a CUSTOMER did something.
    /// </summary>
    /// <remarks>
    /// Nobody is excluded, because the actor is not one of them.
    ///
    /// And nobody is named. Every other row here snapshots the actor's name so the line still reads
    /// correctly after that person is renamed or leaves; doing the same with a customer would copy
    /// their name into a table that is never deleted from, which is a promise about their data this
    /// platform has not made (spec 7). The dealership can see whose booking it is on the booking
    /// itself, where it belongs and where deleting an account removes it. So the row says "a
    /// customer" and carries no actor id at all.
    /// </remarks>
    public Task NotifyTeamOfCustomerActionAsync(
        Dealer dealer,
        NotificationKind kind,
        DateTimeOffset now,
        Id? subjectId = null,
        string? subjectReference = null)
    {
        ArgumentNullException.ThrowIfNull(dealer);
        ArgumentNullException.ThrowIfNull(kind);

        var recipients = Recipients(dealer, exceptUserId: Id.Empty);
        if (recipients.Count == 0)
            return Task.CompletedTask;

        notifier.RaiseMany(recipients.Select(recipient =>
            Notification.RaiseByStandIn(recipient, kind, NotificationStandIn.Customer, now, subjectId, subjectReference)));

        return Task.CompletedTask;
    }

    /// <summary>
    /// Everyone at <paramref name="dealer"/>, told what the PLATFORM did to one of its bookings: an expiry, a no-show,
    /// a completion, an administrator's cancellation or dispute decision (Fix & Polish Wave 3, C5).
    /// </summary>
    /// <remarks>
    /// Named Khadra, with no actor id, so the line reads as the platform's in both languages and never as "a
    /// customer" (the settlement sweep used to report a completion that way, in English inside Arabic). Nobody is
    /// excluded: no colleague acted. Staged, not saved.
    /// </remarks>
    public Task NotifyTeamFromPlatformAsync(
        Dealer dealer,
        NotificationKind kind,
        DateTimeOffset now,
        Id? subjectId = null,
        string? subjectReference = null)
    {
        ArgumentNullException.ThrowIfNull(dealer);
        ArgumentNullException.ThrowIfNull(kind);

        RaiseFromPlatform(Recipients(dealer, exceptUserId: Id.Empty), kind, now, subjectId, subjectReference);
        return Task.CompletedTask;
    }

    /// <summary>
    /// The owner and every active employee granted reports, told what the payouts ledger did with the office's money
    /// (Wave 3, C5): the ledger is theirs to read, and nobody else at the office can open it. Named Khadra. Staged,
    /// not saved.
    /// </summary>
    public Task NotifyReportReadersFromPlatformAsync(
        Dealer dealer,
        NotificationKind kind,
        DateTimeOffset now,
        Id? subjectId = null,
        string? subjectReference = null)
    {
        ArgumentNullException.ThrowIfNull(dealer);
        ArgumentNullException.ThrowIfNull(kind);

        var readers = Recipients(dealer, exceptUserId: Id.Empty).Where(dealer.CanViewReports).ToList();
        RaiseFromPlatform(readers, kind, now, subjectId, subjectReference);
        return Task.CompletedTask;
    }

    /// <summary>
    /// One named person, told about something that happened TO them — a grant, a deactivation.
    ///
    /// Separate from the team feed because the recipient is the subject, not a bystander, and because
    /// the actor here is their owner or an administrator rather than a colleague.
    /// </summary>
    public async Task NotifyPersonAsync(
        Id recipientUserId,
        Id? actorUserId,
        NotificationKind kind,
        DateTimeOffset now,
        Id? subjectId = null,
        string? subjectReference = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(kind);

        if (recipientUserId.IsEmpty || recipientUserId == actorUserId)
            return;

        if (actorUserId is not { } actor)
        {
            notifier.Raise(Notification.Raise(
                recipientUserId, kind, Notification.PlatformActorName, now, subjectId, subjectReference));
            return;
        }

        var actorName = await NameOfAsync(actor, cancellationToken);
        notifier.Raise(ByPerson(recipientUserId, kind, actorName, now, subjectId, subjectReference, actor));
    }

    /// <summary>
    /// The CUSTOMER, told what a gallery did to their booking.
    /// </summary>
    /// <remarks>
    /// The actor is the gallery, named by its business name rather than by the member of staff who
    /// pressed the button. Which employee answered is the dealership's internal business — the team
    /// feed already tells THEM — and the business name is something the customer sees on the booking
    /// anyway. It is passed in rather than looked up because the caller has the dealer loaded and a
    /// second read for a name it already holds is waste.
    ///
    /// No actor id travels with it, for the same reason a customer is never named on a dealer's row:
    /// notifications are not deleted from, and an id that outlives the account it points at is a
    /// dangling reference nothing can resolve.
    ///
    /// Staged, not saved — like everything else here.
    /// </remarks>
    public Task NotifyCustomerAsync(
        Id customerUserId,
        string galleryName,
        NotificationKind kind,
        DateTimeOffset now,
        Id? subjectId = null,
        string? subjectReference = null,
        DateTimeOffset? dueAt = null)
    {
        ArgumentNullException.ThrowIfNull(kind);

        if (customerUserId.IsEmpty)
            return Task.CompletedTask;

        // A gallery removed from the platform between the action and the notification has no name to give. The row
        // still has to render, and "the rental office" is truer than a blank — as a code the clients word (item 103).
        notifier.Raise(string.IsNullOrWhiteSpace(galleryName)
            ? Notification.RaiseByStandIn(customerUserId, kind, NotificationStandIn.RentalOffice, now, subjectId, subjectReference, dueAt: dueAt)
            : Notification.Raise(customerUserId, kind, galleryName.Trim(), now, subjectId, subjectReference, dueAt: dueAt));

        return Task.CompletedTask;
    }

    /// <summary>
    /// The CUSTOMER, told what the PLATFORM did to their booking: an administrator's dispute decision, or a refund the
    /// platform made (Wave 2 C6; E2E F48). Named Khadra, never the rental office: the office did not do it, and a
    /// customer told that the office refunded them would thank or blame the wrong party. Staged, not saved.
    /// </summary>
    public Task NotifyCustomerFromPlatformAsync(
        Id customerUserId,
        NotificationKind kind,
        DateTimeOffset now,
        Id? subjectId = null,
        string? subjectReference = null,
        DateTimeOffset? dueAt = null) =>
        NotifyCustomerAsync(customerUserId, Notification.PlatformActorName, kind, now, subjectId, subjectReference, dueAt);

    private void RaiseFromPlatform(
        List<Id> recipients,
        NotificationKind kind,
        DateTimeOffset now,
        Id? subjectId,
        string? subjectReference)
    {
        if (recipients.Count == 0)
            return;

        notifier.RaiseMany(recipients.Select(recipient =>
            Notification.Raise(recipient, kind, Notification.PlatformActorName, now, subjectId, subjectReference)));
    }

    /// <summary>The owner and every ACTIVE employee. A deactivated one has no standing (spec 4.2).</summary>
    private static List<Id> Recipients(Dealer dealer, Id exceptUserId)
    {
        var recipients = new List<Id>();
        if (dealer.OwnerUserId != exceptUserId)
            recipients.Add(dealer.OwnerUserId);

        recipients.AddRange(dealer.Employees
            .Where(employee => employee.IsActive && employee.UserId != exceptUserId)
            .Select(employee => employee.UserId));

        return recipients;
    }

    /// <summary>
    /// The actor's name at the moment they acted, or null when their account cannot be read.
    ///
    /// A missing user is possible — soft-deleted, or a race with deactivation — and is not worth
    /// failing an approved booking over, so it degrades to a stand-in rather than throwing.
    /// </summary>
    private async Task<string?> NameOfAsync(Id userId, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(userId, cancellationToken);
        return user?.Name.Value;
    }

    /// <summary>
    /// A row naming the person who acted, or — when their name could not be read — the colleague stand-in, as a code the
    /// screens word (pre-launch item 103) beside the English phrase every row used to carry alone.
    /// </summary>
    private static Notification ByPerson(
        Id recipient,
        NotificationKind kind,
        string? actorName,
        DateTimeOffset now,
        Id? subjectId,
        string? subjectReference,
        Id actorUserId) =>
        actorName is null
            ? Notification.RaiseByStandIn(recipient, kind, NotificationStandIn.Colleague, now, subjectId, subjectReference, actorUserId)
            : Notification.Raise(recipient, kind, actorName, now, subjectId, subjectReference, actorUserId);
}
