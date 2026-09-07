using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Domain.Dealers.Events;
using Khadra.Tests.Support;

namespace Khadra.Tests.Domain.Dealers;

public sealed class DealerApprovalTests
{
    private static readonly DateTimeOffset Now = Build.Now;

    [Fact]
    public void Registration_starts_pending_with_delivery_off_and_cannot_trade()
    {
        var dealer = Build.Dealer();

        Assert.Same(DealerVerificationStatus.PendingReview, dealer.VerificationStatus);
        Assert.False(dealer.CanTrade);
        Assert.False(dealer.Delivery.IsEnabled);
        Assert.Equal(Now, dealer.SubmittedAt);
        Assert.IsType<DealerRegistrationSubmitted>(Assert.Single(dealer.DomainEvents));
    }

    [Fact]
    public void Approval_is_refused_until_every_required_document_is_on_file()
    {
        var dealer = Build.Dealer();
        dealer.AttachDocument(DealerDocumentType.CommercialRegistration, "docs/cr.jpg", Now);

        var tooEarly = dealer.Approve(Id.New(), Now);

        Assert.Equal("dealer.missing_documents", tooEarly.Error.Code);
        Assert.False(dealer.HasAllRequiredDocuments);

        Build.AttachAllDocuments(dealer, Now);
        Assert.True(dealer.Approve(Id.New(), Now).IsSuccess);
        Assert.True(dealer.CanTrade);
    }

    [Fact]
    public void Re_attaching_a_document_type_replaces_the_previous_file()
    {
        var dealer = Build.Dealer();

        dealer.AttachDocument(DealerDocumentType.OwnerIdentity, "docs/old.jpg", Now);
        dealer.AttachDocument(DealerDocumentType.OwnerIdentity, "docs/new.jpg", Now.AddHours(1));

        var document = Assert.Single(dealer.Documents, candidate => candidate.Type == DealerDocumentType.OwnerIdentity);
        Assert.Equal("docs/new.jpg", document.StorageKey);
    }

    [Fact]
    public void Documents_are_locked_once_approved()
    {
        var dealer = Build.ApprovedDealer();

        var result = dealer.AttachDocument(DealerDocumentType.OwnerIdentity, "docs/sneaky.jpg", Now);

        Assert.Equal("dealer.documents_locked", result.Error.Code);
    }

    [Fact]
    public void Clarification_sends_it_back_with_a_note_and_resubmission_restarts_the_sla_clock()
    {
        var dealer = Build.Dealer();
        Build.AttachAllDocuments(dealer);
        var admin = Id.New();

        var clarify = dealer.RequestClarification(admin, "The registration photo is unreadable.", Now);
        Assert.True(clarify.IsSuccess);
        Assert.Same(DealerVerificationStatus.ClarificationNeeded, dealer.VerificationStatus);
        Assert.Equal("The registration photo is unreadable.", dealer.ReviewNote);
        Assert.False(dealer.CanTrade);

        var later = Now.AddDays(1);
        Assert.True(dealer.Resubmit(later, Build.ReviewSla).IsSuccess);
        Assert.Same(DealerVerificationStatus.PendingReview, dealer.VerificationStatus);
        Assert.Null(dealer.ReviewNote);
        Assert.Equal(later, dealer.SubmittedAt);
        // The promise is re-frozen from the moment of resubmission, not carried over.
        Assert.Equal(later.Add(Build.ReviewSla), dealer.ReviewDueAt);
    }

    [Fact]
    public void A_rejected_application_can_be_resubmitted_but_an_approved_one_cannot()
    {
        var rejected = Build.Dealer();
        Build.AttachAllDocuments(rejected);
        rejected.Reject(Id.New(), "Licence expired.", Now);

        Assert.Same(DealerVerificationStatus.Rejected, rejected.VerificationStatus);
        Assert.True(rejected.Resubmit(Now.AddDays(1), Build.ReviewSla).IsSuccess);

        var approved = Build.ApprovedDealer();
        Assert.Equal("dealer.nothing_to_resubmit", approved.Resubmit(Now, Build.ReviewSla).Error.Code);
    }

    [Fact]
    public void Rejection_and_clarification_both_demand_a_reason()
    {
        var dealer = Build.Dealer();
        Build.AttachAllDocuments(dealer);

        Assert.Equal("dealer.reason_required", dealer.Reject(Id.New(), "  ", Now).Error.Code);
        Assert.Equal("dealer.reason_required", dealer.RequestClarification(Id.New(), "", Now).Error.Code);
    }

    [Fact]
    public void An_approved_dealer_cannot_be_approved_again()
    {
        var dealer = Build.ApprovedDealer();

        Assert.Equal("dealer.already_approved", dealer.Approve(Id.New(), Now).Error.Code);
        Assert.Equal("dealer.already_approved", dealer.Reject(Id.New(), "changed my mind", Now).Error.Code);
    }

    [Fact]
    public void The_review_sla_only_counts_while_an_admin_actually_owes_a_decision()
    {
        var dealer = Build.Dealer();

        Assert.False(dealer.IsBreachingReviewSla(Now.AddHours(47)));
        Assert.True(dealer.IsBreachingReviewSla(Now.AddHours(48)));

        Build.AttachAllDocuments(dealer);
        dealer.Approve(Id.New(), Now);
        Assert.False(dealer.IsBreachingReviewSla(Now.AddDays(30)));
    }

    [Fact]
    public void Suspension_stops_trading_without_undoing_the_licence_check()
    {
        var dealer = Build.ApprovedDealer();

        Assert.True(dealer.Suspend(Id.New(), "Repeated non-delivery.", Now).IsSuccess);
        Assert.False(dealer.CanTrade);
        Assert.Equal("dealer.not_approved", dealer.EnsureCanTrade().Error.Code);
        // Still approved, so reactivating does not send them back through review.
        Assert.Same(DealerVerificationStatus.Approved, dealer.VerificationStatus);

        dealer.Reactivate();
        Assert.True(dealer.CanTrade);
        Assert.Null(dealer.SuspensionReason);
    }

    [Fact]
    public void A_deleted_dealer_cannot_trade()
    {
        var dealer = Build.ApprovedDealer();

        Assert.True(dealer.Delete(Now).IsSuccess);
        Assert.False(dealer.CanTrade);
        Assert.Equal("dealer.already_deleted", dealer.Delete(Now).Error.Code);
    }
}

public sealed class DealerDeliveryTests
{
    private static readonly DateTimeOffset Now = Build.Now;

    [Fact]
    public void Delivery_is_off_until_enabled_and_a_radius_is_validated()
    {
        var dealer = Build.ApprovedDealer();

        Assert.False(dealer.CoversLocation(Build.Zarqa));
        Assert.Equal("dealer.invalid_delivery_radius", dealer.EnableDelivery(0m, Money.Jod(8m), Now).Error.Code);
        Assert.Equal("dealer.invalid_delivery_radius", dealer.EnableDelivery(500m, Money.Jod(8m), Now).Error.Code);
    }

    [Fact]
    public void Coverage_is_decided_by_real_distance_from_the_dealer()
    {
        var dealer = Build.ApprovedDealer();
        dealer.EnableDelivery(30m, Money.Jod(8m), Now);

        // Amman to Zarqa is roughly 20 km; Aqaba is several hundred.
        Assert.True(dealer.CoversLocation(Build.Zarqa));
        Assert.False(dealer.CoversLocation(Build.Aqaba));
    }

    [Fact]
    public void Disabling_delivery_revokes_coverage_everywhere()
    {
        var dealer = Build.ApprovedDealer();
        dealer.EnableDelivery(30m, Money.Jod(8m), Now);

        dealer.DisableDelivery(Now);

        Assert.False(dealer.Delivery.IsEnabled);
        Assert.False(dealer.CoversLocation(Build.Zarqa));
    }
}

public sealed class DealerEmployeeTests
{
    private static readonly DateTimeOffset Now = Build.Now;

    [Fact]
    public void Employees_start_active_without_report_access()
    {
        var dealer = Build.ApprovedDealer();

        var employee = dealer.HireEmployee(Id.New(), canViewReports: false, Now).Value;

        Assert.True(employee.IsActive);
        Assert.False(employee.CanViewReports);
        Assert.True(employee.CanActOnBookings);
        Assert.True(dealer.CanActOnBookings(employee.UserId));
        Assert.False(dealer.CanViewReports(employee.UserId));
    }

    [Fact]
    public void The_owner_can_always_act_and_always_sees_reports()
    {
        var owner = Id.New();
        var dealer = Build.ApprovedDealer(ownerUserId: owner);

        Assert.True(dealer.CanActOnBookings(owner));
        Assert.True(dealer.CanViewReports(owner));
    }

    [Fact]
    public void The_owner_cannot_be_hired_as_their_own_employee()
    {
        var owner = Id.New();
        var dealer = Build.ApprovedDealer(ownerUserId: owner);

        Assert.Equal("dealer.owner_cannot_be_employee", dealer.HireEmployee(owner, false, Now).Error.Code);
    }

    [Fact]
    public void The_same_user_cannot_be_hired_twice()
    {
        var dealer = Build.ApprovedDealer();
        var user = Id.New();
        dealer.HireEmployee(user, false, Now);

        Assert.Equal("dealer.employee_exists", dealer.HireEmployee(user, true, Now).Error.Code);
    }

    [Fact]
    public void Deactivation_stops_access_immediately_but_keeps_the_record()
    {
        var dealer = Build.ApprovedDealer();
        var employee = dealer.HireEmployee(Id.New(), true, Now).Value;

        Assert.True(dealer.DeactivateEmployee(employee.Id, Now).IsSuccess);

        Assert.False(dealer.CanActOnBookings(employee.UserId));
        Assert.False(dealer.CanViewReports(employee.UserId));
        Assert.Single(dealer.Employees);
        Assert.Equal(Now, employee.DeactivatedAt);
        Assert.Equal("dealer.employee_inactive", dealer.DeactivateEmployee(employee.Id, Now).Error.Code);
    }

    [Fact]
    public void Report_access_is_granted_and_revoked_by_the_owner()
    {
        var dealer = Build.ApprovedDealer();
        var employee = dealer.HireEmployee(Id.New(), canViewReports: false, Now).Value;

        dealer.SetEmployeeReportAccess(employee.Id, true);
        Assert.True(dealer.CanViewReports(employee.UserId));

        dealer.SetEmployeeReportAccess(employee.Id, false);
        Assert.False(dealer.CanViewReports(employee.UserId));
    }

    [Fact]
    public void Staff_of_a_suspended_dealer_cannot_act_on_bookings()
    {
        var dealer = Build.ApprovedDealer();
        var employee = dealer.HireEmployee(Id.New(), false, Now).Value;
        dealer.Suspend(Id.New(), "fraud", Now);

        Assert.False(dealer.CanActOnBookings(employee.UserId));
        Assert.False(dealer.CanActOnBookings(dealer.OwnerUserId));
    }

    [Fact]
    public void Acting_on_an_unknown_employee_reports_not_found()
    {
        var dealer = Build.ApprovedDealer();

        Assert.Equal("dealer.employee_not_found", dealer.DeactivateEmployee(Id.New(), Now).Error.Code);
        Assert.Equal("dealer.employee_not_found", dealer.SetEmployeeReportAccess(Id.New(), true).Error.Code);
    }
}
