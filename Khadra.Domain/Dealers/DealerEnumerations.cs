using Khadra.Domain.Common;

namespace Khadra.Domain.Dealers;

// Spec 3.1: three review outcomes, not two. ClarificationNeeded sends the application back with a
// specific note instead of forcing a full re-application.
public sealed class DealerVerificationStatus : Enumeration
{
    public static readonly DealerVerificationStatus PendingReview = new(1, "PendingReview");
    public static readonly DealerVerificationStatus Approved = new(2, "Approved");
    public static readonly DealerVerificationStatus Rejected = new(3, "Rejected");
    public static readonly DealerVerificationStatus ClarificationNeeded = new(4, "ClarificationNeeded");

    private DealerVerificationStatus(int id, string name) : base(id, name)
    {
    }

    public bool CanTrade => this == Approved;

    public bool IsAwaitingAdmin => this == PendingReview;
}

public sealed class DealerDocumentType : Enumeration
{
    public static readonly DealerDocumentType CommercialRegistration = new(1, "CommercialRegistration");
    public static readonly DealerDocumentType VehicleRegistration = new(2, "VehicleRegistration");
    public static readonly DealerDocumentType OwnerIdentity = new(3, "OwnerIdentity");

    private DealerDocumentType(int id, string name) : base(id, name)
    {
    }

    // Every one of these must be on file before an admin may approve the dealer (spec 3.1).
    public static IReadOnlyList<DealerDocumentType> Required => GetAll<DealerDocumentType>();
}
