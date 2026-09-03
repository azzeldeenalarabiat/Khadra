using CSharpFunctionalExtensions;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers.Events;

namespace Khadra.Domain.Dealers;

// A licensed Jordanian rental office. The Admin gatekeeps legitimacy (spec 3.1), the owner runs the
// business, and employees act on bookings (spec 4.2).
//
// Verification and suspension are deliberately separate: verification is the one-time licence check,
// suspension is an ongoing policy sanction (spec 3.2). A suspended dealer stays "Approved" so that
// reactivating does not send them back through licence review.
public sealed class Dealer : AggregateRoot, ISoftDeletable
{
    private readonly List<Employee> _employees = [];
    private readonly List<DealerDocument> _documents = [];

    public Id OwnerUserId { get; private set; }
    public BusinessName BusinessName { get; private set; } = null!;
    public string? Description { get; private set; }
    public CommercialRegistrationNumber CommercialRegistration { get; private set; } = null!;
    public GeoPoint Location { get; private set; } = null!;
    public Id? CityId { get; private set; }
    public OperatingHours OperatingHours { get; private set; } = null!;
    public DealerVerificationStatus VerificationStatus { get; private set; } = null!;
    // Rejection reason or clarification note from the last admin decision.
    public string? ReviewNote { get; private set; }
    public Id? ReviewedByAdminId { get; private set; }
    public DateTimeOffset? ReviewedAt { get; private set; }
    // Drives the 48-hour admin SLA reminder (spec 3.1).
    public DateTimeOffset SubmittedAt { get; private set; }
    // The SLA promise, frozen at submission rather than recomputed from the current setting.
    //
    // A dispute ticket already freezes its own deadline (DisputeTicket.SlaDeadline). If a dealer
    // application derived its deadline from the live setting instead, shortening the SLA would leave
    // open disputes honouring the old promise while pending applications became retroactively
    // overdue. Freezing both also makes the admin queue an indexable `WHERE review_due_at < now`
    // rather than arithmetic across every row.
    public DateTimeOffset ReviewDueAt { get; private set; }
    public DeliverySettings Delivery { get; private set; } = null!;
    public string? LogoStorageKey { get; private set; }
    public string? CoverStorageKey { get; private set; }
    public bool IsSuspended { get; private set; }
    public string? SuspensionReason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    public IReadOnlyCollection<Employee> Employees => _employees.AsReadOnly();
    public IReadOnlyCollection<DealerDocument> Documents => _documents.AsReadOnly();

    private Dealer()
    {
    }

    private Dealer(Id id) : base(id)
    {
    }

    public static Dealer Register(
        Id ownerUserId,
        BusinessName businessName,
        CommercialRegistrationNumber commercialRegistration,
        GeoPoint location,
        OperatingHours operatingHours,
        DateTimeOffset now,
        TimeSpan reviewSla,
        string? description = null,
        Id? cityId = null)
    {
        ArgumentNullException.ThrowIfNull(businessName);
        ArgumentNullException.ThrowIfNull(commercialRegistration);
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(operatingHours);
        if (ownerUserId.IsEmpty)
            throw new DomainException("A dealer requires an owner.");

        var dealer = new Dealer(Id.New())
        {
            OwnerUserId = ownerUserId,
            BusinessName = businessName,
            CommercialRegistration = commercialRegistration,
            Location = location,
            OperatingHours = operatingHours,
            Description = Trim(description, 2000),
            CityId = cityId,
            VerificationStatus = DealerVerificationStatus.PendingReview,
            Delivery = DeliverySettings.Disabled,
            SubmittedAt = now,
            ReviewDueAt = now.Add(reviewSla),
            CreatedAt = now
        };
        dealer.AddDomainEvent(new DealerRegistrationSubmitted(dealer.Id, ownerUserId, now));
        return dealer;
    }

    // The single question every other context asks before letting this dealer trade (spec 3.1, 4.3).
    public bool CanTrade => VerificationStatus.CanTrade && !IsSuspended && !IsDeleted;

    public UnitResult<Error> EnsureCanTrade() =>
        CanTrade ? UnitResult.Success<Error>() : UnitResult.Failure(DealerErrors.NotApproved);

    public bool HasAllRequiredDocuments =>
        DealerDocumentType.Required.All(required => _documents.Any(document => document.Type == required));

    public UnitResult<Error> AttachDocument(DealerDocumentType type, string storageKey, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(type);

        if (VerificationStatus == DealerVerificationStatus.Approved)
            return UnitResult.Failure(DealerErrors.DocumentsLocked);

        // Re-uploading a document type replaces the previous file: the admin reviews the latest only.
        _documents.RemoveAll(document => document.Type == type);
        _documents.Add(DealerDocument.Attach(Id, type, storageKey, now));
        return UnitResult.Success<Error>();
    }

    public UnitResult<Error> Approve(Id adminUserId, DateTimeOffset now)
    {
        if (VerificationStatus == DealerVerificationStatus.Approved)
            return UnitResult.Failure(DealerErrors.AlreadyApproved);
        if (VerificationStatus == DealerVerificationStatus.Rejected)
            return UnitResult.Failure(DealerErrors.NotAwaitingReview);
        if (!HasAllRequiredDocuments)
            return UnitResult.Failure(DealerErrors.MissingRequiredDocuments);

        VerificationStatus = DealerVerificationStatus.Approved;
        ReviewNote = null;
        RecordReview(adminUserId, now);
        AddDomainEvent(new DealerApproved(Id, adminUserId, now));
        return UnitResult.Success<Error>();
    }

    public UnitResult<Error> Reject(Id adminUserId, string reason, DateTimeOffset now)
    {
        if (VerificationStatus == DealerVerificationStatus.Approved)
            return UnitResult.Failure(DealerErrors.AlreadyApproved);
        if (string.IsNullOrWhiteSpace(reason))
            return UnitResult.Failure(DealerErrors.ReasonRequired);

        VerificationStatus = DealerVerificationStatus.Rejected;
        ReviewNote = Trim(reason, 1000);
        RecordReview(adminUserId, now);
        AddDomainEvent(new DealerRejected(Id, adminUserId, ReviewNote!, now));
        return UnitResult.Success<Error>();
    }

    // The middle outcome (spec 3.1): send it back with a specific note rather than rejecting outright.
    public UnitResult<Error> RequestClarification(Id adminUserId, string note, DateTimeOffset now)
    {
        if (!VerificationStatus.IsAwaitingAdmin)
            return UnitResult.Failure(DealerErrors.NotAwaitingReview);
        if (string.IsNullOrWhiteSpace(note))
            return UnitResult.Failure(DealerErrors.ReasonRequired);

        VerificationStatus = DealerVerificationStatus.ClarificationNeeded;
        ReviewNote = Trim(note, 1000);
        RecordReview(adminUserId, now);
        AddDomainEvent(new DealerClarificationRequested(Id, adminUserId, ReviewNote!, now));
        return UnitResult.Success<Error>();
    }

    // Restarts the 48-hour SLA clock, which is why SubmittedAt and ReviewDueAt are both reset here.
    // A resubmission is a fresh promise, so it takes the SLA in force at that moment.
    public UnitResult<Error> Resubmit(DateTimeOffset now, TimeSpan reviewSla)
    {
        if (VerificationStatus != DealerVerificationStatus.ClarificationNeeded &&
            VerificationStatus != DealerVerificationStatus.Rejected)
        {
            return UnitResult.Failure(DealerErrors.NothingToResubmit);
        }

        VerificationStatus = DealerVerificationStatus.PendingReview;
        ReviewNote = null;
        SubmittedAt = now;
        ReviewDueAt = now.Add(reviewSla);
        AddDomainEvent(new DealerResubmitted(Id, now));
        return UnitResult.Success<Error>();
    }

    // Judged against the promise made at submission, not against the SLA currently configured.
    public bool IsBreachingReviewSla(DateTimeOffset now) =>
        VerificationStatus.IsAwaitingAdmin && now >= ReviewDueAt;

    public UnitResult<Error> Suspend(Id adminUserId, string reason, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return UnitResult.Failure(DealerErrors.ReasonRequired);
        if (IsSuspended)
            return UnitResult.Success<Error>();

        IsSuspended = true;
        SuspensionReason = Trim(reason, 1000);
        AddDomainEvent(new DealerSuspended(Id, adminUserId, SuspensionReason!, now));
        return UnitResult.Success<Error>();
    }

    public void Reactivate()
    {
        IsSuspended = false;
        SuspensionReason = null;
    }

    public UnitResult<Error> EnableDelivery(decimal radiusKm, DateTimeOffset now)
    {
        var settings = DeliverySettings.Enabled(radiusKm);
        if (settings.IsFailure)
            return UnitResult.Failure(settings.Error);

        Delivery = settings.Value;
        AddDomainEvent(new DealerDeliveryChanged(Id, true, Delivery.RadiusKm, now));
        return UnitResult.Success<Error>();
    }

    public void DisableDelivery(DateTimeOffset now)
    {
        Delivery = DeliverySettings.Disabled;
        AddDomainEvent(new DealerDeliveryChanged(Id, false, 0m, now));
    }

    // Spec 5.2: a delivery booking is only offered when the pin falls inside the dealer's radius.
    public bool CoversLocation(GeoPoint destination) => Delivery.Covers(Location, destination);

    public UnitResult<Error> UpdateProfile(
        BusinessName businessName,
        GeoPoint location,
        OperatingHours operatingHours,
        string? description,
        Id? cityId)
    {
        ArgumentNullException.ThrowIfNull(businessName);
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(operatingHours);

        // The licence check (spec 3.1) verified THIS name against the commercial registration. Once
        // approved, changing it would quietly change what that approval meant, so it is locked; an
        // applicant still fixing their submission may change it freely. (Owner decision pending on
        // whether an approved dealer may rename with an admin record; the default here is no.)
        if (VerificationStatus == DealerVerificationStatus.Approved && businessName != BusinessName)
            return UnitResult.Failure(DealerErrors.BusinessNameLocked);

        BusinessName = businessName;
        Location = location;
        OperatingHours = operatingHours;
        Description = Trim(description, 2000);
        CityId = cityId;
        return UnitResult.Success<Error>();
    }

    /// <summary>Replaces the logo; returns the key it replaced so the caller can delete the old file after commit.</summary>
    public string? SetLogo(string storageKey)
    {
        var superseded = LogoStorageKey;
        LogoStorageKey = Trim(storageKey, 500);
        return superseded;
    }

    /// <summary>Replaces the cover image; returns the key it replaced.</summary>
    public string? SetCover(string storageKey)
    {
        var superseded = CoverStorageKey;
        CoverStorageKey = Trim(storageKey, 500);
        return superseded;
    }

    public Result<Employee, Error> HireEmployee(Id userId, bool canViewReports, DateTimeOffset now)
    {
        if (userId.IsEmpty)
            throw new DomainException("An employee requires a user.");
        if (userId == OwnerUserId)
            return DealerErrors.OwnerCannotBeEmployee;
        // The uniqueness rule covers deactivated employees too: re-hiring reactivates the existing row
        // so the audit trail of their past booking decisions stays attached to one identity.
        if (_employees.Any(employee => employee.UserId == userId))
            return DealerErrors.EmployeeAlreadyExists;

        var hired = Employee.Hire(Id, userId, canViewReports, now);
        _employees.Add(hired);
        AddDomainEvent(new DealerEmployeeHired(Id, hired.Id, userId, now));
        return hired;
    }

    public UnitResult<Error> DeactivateEmployee(Id employeeId, DateTimeOffset now)
    {
        var employee = _employees.SingleOrDefault(candidate => candidate.Id == employeeId);
        if (employee is null)
            return UnitResult.Failure(DealerErrors.EmployeeNotFound);
        if (!employee.IsActive)
            return UnitResult.Failure(DealerErrors.EmployeeAlreadyInactive);

        employee.Deactivate(now);
        AddDomainEvent(new DealerEmployeeDeactivated(Id, employee.Id, employee.UserId, now));
        return UnitResult.Success<Error>();
    }

    public UnitResult<Error> ReactivateEmployee(Id employeeId)
    {
        var employee = _employees.SingleOrDefault(candidate => candidate.Id == employeeId);
        if (employee is null)
            return UnitResult.Failure(DealerErrors.EmployeeNotFound);

        employee.Reactivate();
        return UnitResult.Success<Error>();
    }

    public UnitResult<Error> SetEmployeeReportAccess(Id employeeId, bool canViewReports)
    {
        var employee = _employees.SingleOrDefault(candidate => candidate.Id == employeeId);
        if (employee is null)
            return UnitResult.Failure(DealerErrors.EmployeeNotFound);

        employee.SetReportAccess(canViewReports);
        return UnitResult.Success<Error>();
    }

    // Who may approve or reject this dealer's bookings: the owner, or an active employee (spec 4.2).
    public bool CanActOnBookings(Id userId) =>
        CanTrade && (userId == OwnerUserId ||
                     _employees.Any(employee => employee.UserId == userId && employee.CanActOnBookings));

    // Financial reports are the owner's by default; employees need the explicit grant (spec 4.5).
    public bool CanViewReports(Id userId) =>
        userId == OwnerUserId ||
        _employees.Any(employee => employee.UserId == userId && employee.IsActive && employee.CanViewReports);

    public UnitResult<Error> Delete(DateTimeOffset now)
    {
        if (IsDeleted)
            return UnitResult.Failure(DealerErrors.AlreadyDeleted);

        IsDeleted = true;
        DeletedAt = now;
        return UnitResult.Success<Error>();
    }

    private void RecordReview(Id adminUserId, DateTimeOffset now)
    {
        ReviewedByAdminId = adminUserId;
        ReviewedAt = now;
    }

    private static string? Trim(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
