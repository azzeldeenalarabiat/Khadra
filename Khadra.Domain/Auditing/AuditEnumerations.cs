using Khadra.Domain.Common;

namespace Khadra.Domain.Auditing;

// The closed vocabulary of privileged actions the platform records.
//
// A smart enum rather than a free string on purpose: the audit screen and the activity feed both have
// to turn an action into readable text, and a controlled vocabulary makes that mapping total. Adding
// an action becomes a deliberate act, which is what an audit trail wants.
//
// ADD-ONLY (pre-launch item 19): rows store the NAME and are read back through Enumeration.FromName, which throws
// for a name that no longer exists. Renaming or removing a member makes every row carrying it unreadable, and audit
// rows cannot be corrected. Add members; never rename or remove one. PersistedEnumerationNamesTests holds the list.
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
    // Deactivating an administrator has to be reversible, and the reversal has to be on the record.
    public static readonly AuditAction AdminReactivated = new(19, "AdminReactivated");

    // Curating the cities and car types is a privileged admin action like any other: retiring a city
    // takes it off every new listing and every customer search, and there is no delete to undo it.
    // Generic on purpose — one set of verbs serves both lists, and the entity type says which.
    public static readonly AuditAction LookupCreated = new(20, "LookupCreated");
    public static readonly AuditAction LookupRenamed = new(21, "LookupRenamed");
    public static readonly AuditAction LookupRetired = new(22, "LookupRetired");
    public static readonly AuditAction LookupRestored = new(23, "LookupRestored");

    // Reissuing an administrator's invitation mints a new credential for an account that can
    // administer the platform, and retires the previous one. The invitation itself is already on the
    // record (AdminInvited); this says somebody made a second key for the same door, and who.
    public static readonly AuditAction AdminInvitationResent = new(24, "AdminInvitationResent");

    // The car changed hands. Recorded by dealer staff, proved by the customer's one-time code or,
    // when there was none, recorded as unverified with the dealer's reason. The unverified ones are
    // what the platform reviews.
    public static readonly AuditAction HandoverVerified = new(25, "HandoverVerified");
    public static readonly AuditAction HandoverUnverified = new(26, "HandoverUnverified");
    // Enough wrong codes were typed for one booking to lock the customer's code.
    public static readonly AuditAction HandoverCodeLocked = new(27, "HandoverCodeLocked");

    // An administrator voided an issued financial document (payments Phase 5). The void and its
    // correction are one transaction with this entry; the previous and new values are the two numbers.
    public static readonly AuditAction FinancialDocumentVoided = new(28, "FinancialDocumentVoided");

    // An administrator asked for an issued receipt to be emailed to its customer again (payments Phase 7).
    // Labelled by the document's number, like the void: an entry that can never be erased names no customer
    // and no address.
    public static readonly AuditAction FinancialDocumentEmailRequested = new(29, "FinancialDocumentEmailRequested");

    // An administrator recorded money moved to or from a rental office by hand, or voided that record (payments
    // Phase 8). Labelled by the settlement's number; the values are the signed amount and its currency.
    public static readonly AuditAction OfficeSettlementRecorded = new(30, "OfficeSettlementRecorded");
    public static readonly AuditAction OfficeSettlementVoided = new(31, "OfficeSettlementVoided");

    // An administrator left a booking's payable out of settlements, or let it back in (payments Phase 8). Labelled
    // by the booking's reference, never by a name.
    public static readonly AuditAction OfficePayableHeld = new(32, "OfficePayableHeld");
    public static readonly AuditAction OfficePayableReleased = new(33, "OfficePayableReleased");

    // An administrator published a version of a legal text (Wave 2 G1). Labelled by the version's label; the new value
    // names the document, and the entry is the record of who published it.
    public static readonly AuditAction LegalDocumentPublished = new(34, "LegalDocumentPublished");

    // An administrator marked a capture incident handled: the money a second or contradictory capture took was dealt
    // with at the provider (Wave 4, B1). Labelled by the booking's reference; the new value is the incident's kind,
    // and the reason is the administrator's note.
    public static readonly AuditAction PaymentIncidentHandled = new(35, "PaymentIncidentHandled");

    // An administrator rejected one of a customer's documents (Wave 4, W4-9; checklist 27). Labelled by the customer's
    // short id, never a name; the values carry the document's type with its status before and after
    // ("DrivingLicenceFront:PendingReview" → "DrivingLicenceFront:Rejected"), and the reason is the one the customer reads.
    public static readonly AuditAction CustomerDocumentRejected = new(36, "CustomerDocumentRejected");

    private AuditAction(int id, string name) : base(id, name)
    {
    }
}

// ADD-ONLY (pre-launch item 19): rows store the NAME and are read back through Enumeration.FromName, which throws
// for a name that no longer exists. Renaming or removing a member makes every row carrying it unreadable, and audit
// rows cannot be corrected. Add members; never rename or remove one. PersistedEnumerationNamesTests holds the list.
public sealed class AuditEntityType : Enumeration
{
    public static readonly AuditEntityType Dealer = new(1, "Dealer");
    public static readonly AuditEntityType Customer = new(2, "Customer");
    public static readonly AuditEntityType Booking = new(3, "Booking");
    public static readonly AuditEntityType Dispute = new(4, "Dispute");
    public static readonly AuditEntityType Review = new(5, "Review");
    public static readonly AuditEntityType Setting = new(6, "Setting");
    public static readonly AuditEntityType AdminUser = new(7, "AdminUser");

    // Their own types rather than reusing Setting, which is the business-rules subject: the audit
    // screen filters and deep-links by (entity type, entity id), so "what happened to the cities
    // list" has to be answerable without reading labels.
    public static readonly AuditEntityType City = new(8, "City");
    public static readonly AuditEntityType CarType = new(9, "CarType");

    // A receipt or a booking statement (payments Phase 5), recorded by its id and labelled by its number.
    public static readonly AuditEntityType FinancialDocument = new(10, "FinancialDocument");

    // A settlement with a rental office, and one booking's payable (payments Phase 8).
    public static readonly AuditEntityType OfficeSettlement = new(11, "OfficeSettlement");
    public static readonly AuditEntityType OfficePayable = new(12, "OfficePayable");

    // A published version of a legal text (Wave 2 G1).
    public static readonly AuditEntityType LegalDocument = new(13, "LegalDocument");

    // A capture incident on a payment (Wave 4, B1).
    public static readonly AuditEntityType PaymentIncident = new(14, "PaymentIncident");

    private AuditEntityType(int id, string name) : base(id, name)
    {
    }
}
