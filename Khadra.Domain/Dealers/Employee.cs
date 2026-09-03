using Khadra.Domain.Common;
using Entity = Khadra.Domain.Common.Entity;

namespace Khadra.Domain.Dealers;

// Spec 4.2: employees never self-register. They exist only inside a Dealer, created by its owner.
// Deactivation stops their access immediately but keeps the row so past booking decisions stay attributable.
public sealed class Employee : Entity
{
    public Id DealerId { get; private set; }
    public Id UserId { get; private set; }
    // Off by default; the owner grants it explicitly (spec 4.2).
    public bool CanViewReports { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DeactivatedAt { get; private set; }

    private Employee()
    {
    }

    private Employee(Id id) : base(id)
    {
    }

    internal static Employee Hire(Id dealerId, Id userId, bool canViewReports, DateTimeOffset now)
    {
        if (dealerId.IsEmpty || userId.IsEmpty)
            throw new DomainException("An employee requires a dealer and a user.");

        return new Employee(Id.New())
        {
            DealerId = dealerId,
            UserId = userId,
            CanViewReports = canViewReports,
            IsActive = true,
            CreatedAt = now
        };
    }

    internal void Deactivate(DateTimeOffset now)
    {
        IsActive = false;
        DeactivatedAt ??= now;
    }

    internal void Reactivate()
    {
        IsActive = true;
        DeactivatedAt = null;
    }

    internal void SetReportAccess(bool canViewReports) => CanViewReports = canViewReports;

    // Default employee permission is approving and rejecting booking requests (spec 4.2).
    public bool CanActOnBookings => IsActive;
}

// Private evidence for the Admin's licence check (spec 3.1). Only the storage key lives here; the file
// itself sits in access-controlled storage and is served through short-lived signed URLs (spec 7).
public sealed class DealerDocument : Entity
{
    public Id DealerId { get; private set; }
    public DealerDocumentType Type { get; private set; } = null!;
    public string StorageKey { get; private set; } = null!;
    public DateTimeOffset UploadedAt { get; private set; }

    private DealerDocument()
    {
    }

    private DealerDocument(Id id) : base(id)
    {
    }

    internal static DealerDocument Attach(Id dealerId, DealerDocumentType type, string storageKey, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (dealerId.IsEmpty)
            throw new DomainException("A document requires a dealer.");
        if (string.IsNullOrWhiteSpace(storageKey) || storageKey.Length > 500)
            throw new DomainException("A document storage key is required.");

        return new DealerDocument(Id.New())
        {
            DealerId = dealerId,
            Type = type,
            StorageKey = storageKey.Trim(),
            UploadedAt = now
        };
    }
}
