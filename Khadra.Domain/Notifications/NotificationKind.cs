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
// transaction. Kinds the design draws but nothing can raise (a customer cancellation, "pickup
// approaching") are deliberately absent: there is no customer-cancellation endpoint and no scheduler.
// They arrive with their producers, not before, because a kind nothing raises is a promise of an
// alert that never comes.
public sealed class NotificationKind : Enumeration
{
    // Declared FIRST: static fields initialise in order, and every kind below reads these.
    //
    // Which channels a kind is delivered on besides the in-app list. Only the customer's own kinds
    // wake a phone: staff work in the console, which they have open, and a push for every colleague's
    // click would train them to ignore the ones that matter. Email is reserved for kinds a customer
    // must act on while away from the app (the reminders), because an inbox that fills with every
    // status change stops being read.
    private static readonly NotificationChannel[] None = [];
    private static readonly NotificationChannel[] PushOnly = [NotificationChannel.Push];
    private static readonly NotificationChannel[] PushAndEmail = [NotificationChannel.Push, NotificationChannel.Email];

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

    // Changes to one person's own standing (EmployeeHandlers).
    public static readonly NotificationKind StaffReactivated = new(10, "StaffReactivated");
    public static readonly NotificationKind ReportAccessGranted = new(11, "ReportAccessGranted");
    public static readonly NotificationKind ReportAccessRevoked = new(12, "ReportAccessRevoked");

    // Deliberately ABSENT, each for a reason rather than an oversight:
    //
    //   StaffInvited      — an invited account is inert until the link is accepted; there is nobody
    //                       to receive it, and the invitation itself goes by email.
    //   StaffDeactivated  — deactivating rotates the security stamp and ends every session that
    //                       instant (spec 4.2), so the row could never be seen by its recipient.
    //   BookingCancelledByAdmin, DisputeOpened, DisputeResolved — for the DEALER team: real and
    //                       worth adding, and their handlers exist; not wired yet. The customer's
    //                       side of both now is (YourBookingCancelled, YourDisputeUpdated).
    //   YourDepositDue    — the customer IS told their booking was approved, and YourBookingApproved
    //                       is that message. A separate "pay now" alert would still be a promise of a
    //                       payment the platform cannot take: Payments ships with no provider
    //                       configured, so every checkout is refused with payments.provider_unavailable.
    //                       Add it when a provider exists, not before.
    //   YourRefundIssued  — the platform can RECORD a refund but cannot send one without a provider,
    //                       so telling a customer their money is on its way would not be true yet.

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
