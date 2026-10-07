using Khadra.Domain.Common;

namespace Khadra.Domain.Notifications;

// The closed vocabulary of things worth telling someone about.
//
// A smart enum rather than a free string, for the same reason `AuditAction` is one: two screens and
// (later) two languages have to turn a notification into a readable line, and a controlled vocabulary
// makes that mapping total. Adding a kind is a deliberate act, which is what stops this table filling
// with ad-hoc strings nobody can render.
//
// Every kind here has a PRODUCER in this repository today — a handler that raises it inside its own
// transaction. Kinds the design draws but nothing raises (an office's "pickup approaching") are
// deliberately absent. They arrive with their producers, not before, because a kind nothing raises
// is a promise of an alert that never comes.
public sealed class NotificationKind : Enumeration
{
    // Declared FIRST: static fields initialise in order, and every kind below reads these.
    //
    // Which channels a kind is delivered on besides the in-app list. Only the customer's own kinds
    // wake a phone: staff work in the console, which they have open, and a push for every colleague's
    // click would train them to ignore the ones that matter. Email is reserved for kinds a customer
    // must act on while away from the app (the reminders), because an inbox that fills with every
    // status change stops being read -- and, for an office, for what the platform or a customer does
    // to its bookings and its money while nobody there is looking (Fix & Polish Wave 3, C5). An office
    // colleague's own clicks stay in the console.
    private static readonly NotificationChannel[] None = [];
    private static readonly NotificationChannel[] PushOnly = [NotificationChannel.Push];
    private static readonly NotificationChannel[] PushAndEmail = [NotificationChannel.Push, NotificationChannel.Email];
    private static readonly NotificationChannel[] EmailOnly = [NotificationChannel.Email];

    // A customer asked for one of the dealership's cars (CreateBookingHandler). The only kind here
    // raised by someone OUTSIDE the dealership, which is why its row carries no actor: see
    // DealerTeamNotifier.NotifyTeamOfCustomerActionAsync for why a customer is never named on it.
    public static readonly NotificationKind BookingRequested = new(13, "BookingRequested");

    // What a colleague did to a booking the dealership shares (BookingDecisionHandlers).
    public static readonly NotificationKind BookingApproved = new(1, "BookingApproved");
    public static readonly NotificationKind BookingRejected = new(2, "BookingRejected");
    public static readonly NotificationKind BookingPickedUp = new(3, "BookingPickedUp");
    public static readonly NotificationKind BookingReturned = new(4, "BookingReturned");

    // The platform's decisions about the dealership itself (ReviewDealerHandlers). Every one of
    // these changes what the console will let its staff do, so the whole team is told.
    public static readonly NotificationKind DealerApproved = new(5, "DealerApproved");
    public static readonly NotificationKind DealerRejected = new(6, "DealerRejected");
    public static readonly NotificationKind DealerClarificationRequested = new(7, "DealerClarificationRequested");
    public static readonly NotificationKind DealerSuspended = new(8, "DealerSuspended");
    public static readonly NotificationKind DealerReactivated = new(9, "DealerReactivated");

    // What a CUSTOMER did to a booking of the dealership's (CancelBookingHandlers). Raised through
    // NotifyTeamOfCustomerActionAsync, so like BookingRequested the row names "A customer" and
    // carries no actor id.
    public static readonly NotificationKind BookingCancelledByCustomer = new(14, "BookingCancelledByCustomer");
    public static readonly NotificationKind BookingNonDeliveryReported = new(15, "BookingNonDeliveryReported");

    // What happened to the CUSTOMER's own booking (BookingDecisionHandlers, BookingSettlementService).
    // The recipient is the customer, and the actor name is the GALLERY's business name rather than a
    // member of its staff: which employee pressed the button is the dealership's internal business,
    // and the customer already sees the gallery on the booking.
    public static readonly NotificationKind YourBookingApproved = new(16, "YourBookingApproved", PushOnly);
    public static readonly NotificationKind YourBookingRejected = new(17, "YourBookingRejected", PushOnly);
    public static readonly NotificationKind YourBookingExpired = new(18, "YourBookingExpired", PushOnly);
    public static readonly NotificationKind YourBookingCompleted = new(19, "YourBookingCompleted", PushOnly);
    public static readonly NotificationKind YourBookingMarkedNoShow = new(20, "YourBookingMarkedNoShow", PushOnly);

    // The deposit cleared and the rental is on (ReceiveProviderEventHandler). The gallery learns it
    // has a committed customer; the customer learns their money arrived. Both are raised inside the
    // same transaction as the capture, so a notification can never claim a payment that rolled back.
    public static readonly NotificationKind BookingConfirmed = new(21, "BookingConfirmed");
    public static readonly NotificationKind YourBookingConfirmed = new(22, "YourBookingConfirmed", PushOnly);

    // The platform cancelled the customer's booking (AdminBookingCommandHandlers). A customer who
    // cancels their own is not told what they just did; a gallery cannot cancel, it rejects.
    public static readonly NotificationKind YourBookingCancelled = new(23, "YourBookingCancelled", PushOnly);

    // An administrator picked up or decided the dispute on the customer's booking
    // (AdminDisputeHandlers). Its subject is the TICKET, so tapping it opens the dispute; the
    // reference is the booking's, which is what the customer recognises.
    public static readonly NotificationKind YourDisputeUpdated = new(24, "YourDisputeUpdated", PushOnly);

    // Reminders (SendDueRemindersHandler, in the settlement pass). Push AND email: these are the
    // messages a customer must act on while away from the app, and a phone left on silent should not
    // cost them a car. Each carries its anchor in DueAt, so the time it states is the booking's own.
    public static readonly NotificationKind YourPaymentReminder = new(25, "YourPaymentReminder", PushAndEmail);
    public static readonly NotificationKind YourPickupReminder = new(26, "YourPickupReminder", PushAndEmail);
    public static readonly NotificationKind YourReturnReminder = new(27, "YourReturnReminder", PushAndEmail);

    // The car changed hands (BookingDecisionHandlers). Since the handover code, this is how the
    // customer's phone learns the code it is showing has done its job: the push lands, the booking
    // re-reads, and the code screen gives way to the rental.
    public static readonly NotificationKind YourBookingPickedUp = new(28, "YourBookingPickedUp", PushOnly);
    public static readonly NotificationKind YourBookingReturned = new(29, "YourBookingReturned", PushOnly);

    // The provider says the deposit a free cancellation returned has been refunded (owner, 2026-09-24;
    // ReceiveProviderEventHandlers). Sent on SETTLEMENT, never on the request: "your money is on its
    // way" is already on the booking the moment it is cancelled, and a notification is for the news
    // the customer is waiting for. By email too, because a refund is a record people keep.
    public static readonly NotificationKind YourDepositRefunded = new(30, "YourDepositRefunded", PushAndEmail);

    // A refund has reached the customer and more of the payment is still out or held (Phase 3,
    // 2026-09-26): the money above the deposit when a booking ended before pickup, a dispute's share.
    // YourDepositRefunded is kept for the moment EVERYTHING the payment will return is back, so a
    // customer is never told "your payment has been refunded" while part of it is still held.
    public static readonly NotificationKind YourPartialRefundSettled = new(31, "YourPartialRefundSettled", PushAndEmail);

    // Changes to one person's own standing (EmployeeHandlers).
    public static readonly NotificationKind StaffReactivated = new(10, "StaffReactivated");
    public static readonly NotificationKind ReportAccessGranted = new(11, "ReportAccessGranted");
    public static readonly NotificationKind ReportAccessRevoked = new(12, "ReportAccessRevoked");

    // What happened to the office's bookings and money that no colleague did (Fix & Polish Wave 3, C5;
    // E2E F28 and F55). In the console AND by email, never by push: the office works in the console,
    // and the email is what reaches it when nobody there is looking.
    //
    // A dispute on one of the office's bookings (RaiseDisputeHandlers): opened by the customer, or by
    // a colleague, who is not told of their own. Its subject is the TICKET.
    public static readonly NotificationKind DisputeOpened = new(32, "DisputeOpened", EmailOnly);

    // The platform's own acts, raised through DealerTeamNotifier.NotifyTeamFromPlatformAsync, so each
    // reads as Khadra's and carries no actor id. A decided dispute (AdminDisputeHandlers; subject the
    // ticket); a booking completed by the sweep or by that decision, only when it really moved
    // (SettleDueBookingsHandler, AdminDisputeHandlers); a no-show; an APPROVED booking nobody paid for
    // -- never a request the office did not answer, which the office would be told it let lapse; and
    // an administrator's cancellation (AdminBookingCommandHandlers).
    public static readonly NotificationKind DisputeResolved = new(33, "DisputeResolved", EmailOnly);
    public static readonly NotificationKind BookingCompleted = new(34, "BookingCompleted", EmailOnly);
    public static readonly NotificationKind BookingMarkedNoShow = new(35, "BookingMarkedNoShow", EmailOnly);
    public static readonly NotificationKind BookingExpiredUnpaid = new(36, "BookingExpiredUnpaid", EmailOnly);
    public static readonly NotificationKind BookingCancelledByAdmin = new(37, "BookingCancelledByAdmin", EmailOnly);

    // The payouts ledger recorded or voided a settlement with the office (Record- and
    // VoidOfficeSettlementHandler). Told to the owner and to the employees granted reports only: the
    // ledger is theirs to read. No amount travels: the row holds none, and Payouts shows it.
    public static readonly NotificationKind SettlementRecorded = new(38, "SettlementRecorded", EmailOnly);
    public static readonly NotificationKind SettlementVoided = new(39, "SettlementVoided", EmailOnly);

    // The customer opened a dispute (RaiseDisputeHandlers; Wave 3 D10): what happens now, and by when
    // Khadra aims to decide, its DueAt being the ticket's frozen SLA deadline. Its subject is the
    // BOOKING, not the ticket: installed apps open every kind but YourDisputeUpdated at
    // /bookings/{subject}, and the booking links its dispute. A dispute the OFFICE opens reaches the
    // customer as YourDisputeUpdated, which every installed app already opens at the dispute.
    public static readonly NotificationKind YourDisputeOpened = new(40, "YourDisputeOpened", PushAndEmail);

    // Khadra could not accept one of the customer's documents (Wave 4, W4-9; checklist 27). NO SUBJECT: it is about the
    // account, not a booking, so the push carries no subjectId (every installed app routes a literal "null" to
    // /bookings/null) and no subjectReference (installed apps print it raw under the line). Its words never carry the
    // reason — a lock screen is not a private channel; the documents page says which and why. Installed builds show a
    // generic line and open nothing; routing it to the documents screen is in the app's 1.4.0 ledger.
    public static readonly NotificationKind YourDocumentRejected = new(41, "YourDocumentRejected", PushAndEmail);

    // Deliberately ABSENT, each for a reason rather than an oversight:
    //
    //   StaffInvited      — an invited account is inert until the link is accepted; there is nobody
    //                       to receive it, and the invitation itself goes by email.
    //   StaffDeactivated  — deactivating rotates the security stamp and ends every session that
    //                       instant (spec 4.2), so the row could never be seen by its recipient.
    //   BookingExpiredUnanswered — for the office: a request it let lapse is on its own queue and
    //                       dashboard, and the owner chose not to add it (Wave 3, C7). The customer
    //                       is told (YourBookingExpired).
    //   YourDepositDue    — the customer IS told their booking was approved, and YourBookingApproved
    //                       is that message. A separate "pay now" alert would still be a promise of a
    //                       payment the platform cannot take: Payments ships with no provider
    //                       configured, so every checkout is refused with payments.provider_unavailable.
    //                       Add it when a provider exists, not before.
    //   YourRefundIssued  — superseded on 2026-09-24 by YourDepositRefunded, which is sent only when a
    //                       provider SETTLES the refund, so it is never a promise the platform cannot
    //                       keep. Since Phase 3 every settled refund sends one of the two refund kinds.

    private readonly NotificationChannel[] _channels;

    private NotificationKind(int id, string name, NotificationChannel[]? channels = null) : base(id, name)
    {
        _channels = channels ?? None;
    }

    /// <summary>
    /// The channels this kind is delivered on outside the app. Empty for staff kinds.
    /// </summary>
    /// <remarks>
    /// A METHOD, deliberately: a smart enum carrying a collection of other smart enums as a PROPERTY is
    /// walked by the JSON writer, which is how <c>Language.Other</c> once turned a public page into a 500.
    /// </remarks>
    public IReadOnlyList<NotificationChannel> DeliveredOn() => _channels;
}
