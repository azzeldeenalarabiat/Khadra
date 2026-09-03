using Khadra.Domain.Common;

namespace Khadra.Domain.Auditing;

// The closed vocabulary of privileged actions the platform records.
//
// A smart enum rather than a free string on purpose: the audit screen and the activity feed both have
// to turn an action into readable text, and a controlled vocabulary makes that mapping total. Adding
// an action becomes a deliberate act, which is what an audit trail wants.
public sealed class AuditAction : Enumeration
{
    public static readonly AuditAction DealerApproved = new(1, "DealerApproved");
    public static readonly AuditAction DealerRejected = new(2, "DealerRejected");
    public static readonly AuditAction DealerClarificationRequested = new(3, "DealerClarificationRequested");
    public static readonly AuditAction DealerSuspended = new(4, "DealerSuspended");
    public static readonly AuditAction DealerReactivated = new(5, "DealerReactivated");
    public static readonly AuditAction CustomerSuspended = new(6, "CustomerSuspended");
    public static readonly AuditAction CustomerReactivated = new(7, "CustomerReactivated");
    public static readonly AuditAction DisputeOpened = new(8, "DisputeOpened");
    public static readonly AuditAction DisputeAssigned = new(9, "DisputeAssigned");
    public static readonly AuditAction DisputeResolved = new(10, "DisputeResolved");
    public static readonly AuditAction BusinessRuleChanged = new(11, "BusinessRuleChanged");
    public static readonly AuditAction ReviewHidden = new(12, "ReviewHidden");
    public static readonly AuditAction ReviewRestored = new(13, "ReviewRestored");
    public static readonly AuditAction AdminInvited = new(14, "AdminInvited");
    public static readonly AuditAction AdminDeactivated = new(15, "AdminDeactivated");
    public static readonly AuditAction BookingCancelledByAdmin = new(16, "BookingCancelledByAdmin");
    public static readonly AuditAction BookingExpired = new(17, "BookingExpired");
    public static readonly AuditAction BookingMarkedNoShow = new(18, "BookingMarkedNoShow");

    private AuditAction(int id, string name) : base(id, name)
    {
    }
}

public sealed class AuditEntityType : Enumeration
{
    public static readonly AuditEntityType Dealer = new(1, "Dealer");
    public static readonly AuditEntityType Customer = new(2, "Customer");
    public static readonly AuditEntityType Booking = new(3, "Booking");
    public static readonly AuditEntityType Dispute = new(4, "Dispute");
    public static readonly AuditEntityType Review = new(5, "Review");
    public static readonly AuditEntityType Setting = new(6, "Setting");
    public static readonly AuditEntityType AdminUser = new(7, "AdminUser");

    private AuditEntityType(int id, string name) : base(id, name)
    {
    }
}
