using Khadra.Domain.Auditing;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.Notifications;
using Khadra.Domain.Reviews;

namespace Khadra.Tests.Domain.Common;

/// <summary>
/// The names stored rows are read back by (pre-launch item 19).
/// </summary>
/// <remarks>
/// <para>
/// These enumerations are persisted by NAME and materialised through <see cref="Enumeration.FromName{T}"/>, which throws
/// for a name that no longer exists. Rename or remove one member and every row carrying it becomes unreadable — the
/// audit log fails outright, a user cannot sign in, a review cannot be listed — although the data is still in the table.
/// <c>audit_entries</c> is append-only, so those rows cannot even be corrected.
/// </para>
/// <para>
/// So they are ADD-ONLY once a row can reference them, and this test is the list. Adding a member means adding it here,
/// which is the deliberate act an addition should be; renaming or removing one fails here instead of in production.
/// Renaming is a data migration, never a refactor.
/// </para>
/// </remarks>
public sealed class PersistedEnumerationNamesTests
{
    [Fact]
    public void Audit_actions_are_add_only()
    {
        AssertExactly<AuditAction>(
            (1, "DealerApproved"), (2, "DealerRejected"), (3, "DealerClarificationRequested"), (4, "DealerSuspended"),
            (5, "DealerReactivated"), (6, "CustomerSuspended"), (7, "CustomerReactivated"), (8, "DisputeOpened"),
            (9, "DisputeAssigned"), (10, "DisputeResolved"), (11, "BusinessRuleChanged"), (12, "ReviewHidden"),
            (13, "ReviewRestored"), (14, "AdminInvited"), (15, "AdminDeactivated"), (16, "BookingCancelledByAdmin"),
            (17, "BookingExpired"), (18, "BookingMarkedNoShow"), (19, "AdminReactivated"), (20, "LookupCreated"),
            (21, "LookupRenamed"), (22, "LookupRetired"), (23, "LookupRestored"), (24, "AdminInvitationResent"),
            (25, "HandoverVerified"), (26, "HandoverUnverified"), (27, "HandoverCodeLocked"),
            (28, "FinancialDocumentVoided"), (29, "FinancialDocumentEmailRequested"), (30, "OfficeSettlementRecorded"),
            (31, "OfficeSettlementVoided"), (32, "OfficePayableHeld"), (33, "OfficePayableReleased"),
            (34, "LegalDocumentPublished"), (35, "PaymentIncidentHandled"), (36, "CustomerDocumentRejected"));
    }

    [Fact]
    public void Audit_entity_types_are_add_only()
    {
        AssertExactly<AuditEntityType>(
            (1, "Dealer"), (2, "Customer"), (3, "Booking"), (4, "Dispute"), (5, "Review"), (6, "Setting"),
            (7, "AdminUser"), (8, "City"), (9, "CarType"), (10, "FinancialDocument"), (11, "OfficeSettlement"),
            (12, "OfficePayable"), (13, "LegalDocument"), (14, "PaymentIncident"));
    }

    [Fact]
    public void User_roles_are_add_only()
    {
        AssertExactly<UserRole>((1, "Admin"), (2, "DealerOwner"), (3, "DealerEmployee"), (4, "Customer"));
    }

    /// <summary>The policy reason a hidden review is stored under (pre-launch item 81, Wave 6).</summary>
    [Fact]
    public void Review_hide_reasons_are_add_only()
    {
        AssertExactly<ReviewHideReason>(
            (1, "PersonalContactDetails"), (2, "AbusiveLanguage"), (3, "NotAboutThisRental"), (4, "SpamOrPromotion"));
    }

    /// <summary>Who a notification's unnamed actor is (pre-launch item 103, Wave 6).</summary>
    [Fact]
    public void Notification_stand_ins_are_add_only()
    {
        AssertExactly<NotificationStandIn>((1, "Customer"), (2, "Colleague"), (3, "RentalOffice"));
    }

    private static void AssertExactly<T>(params (int Id, string Name)[] expected)
        where T : Enumeration
    {
        var actual = Enumeration.GetAll<T>().Select(member => (member.Id, member.Name)).OrderBy(member => member.Id).ToList();
        Assert.Equal(expected.OrderBy(member => member.Id).ToList(), actual);
        // And each is read back by the name a row stores.
        Assert.All(expected, member => Assert.Equal(member.Id, Enumeration.FromName<T>(member.Name).Id));
    }
}
