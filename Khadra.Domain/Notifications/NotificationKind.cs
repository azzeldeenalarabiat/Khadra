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
    public static readonly NotificationKind YourBookingApproved = new(16, "YourBookingApproved");
    public static readonly NotificationKind YourBookingRejected = new(17, "YourBookingRejected");
    public static readonly NotificationKind YourBookingExpired = new(18, "YourBookingExpired");
    public static readonly NotificationKind YourBookingCompleted = new(19, "YourBookingCompleted");
    public static readonly NotificationKind YourBookingMarkedNoShow = new(20, "YourBookingMarkedNoShow");

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
    //   BookingCancelledByAdmin, DisputeOpened, DisputeResolved — real and worth adding, and their
    //                       handlers exist; they are simply not wired yet. Add the kind WITH its
    //                       producer, never before it.
    //   YourDepositDue    — the customer IS told their booking was approved, and YourBookingApproved
    //                       is that message. A separate "pay now" alert would be a promise of a
    //                       payment screen that does not exist until Payments ships.

    private NotificationKind(int id, string name) : base(id, name)
    {
    }
}
