using Khadra.Domain.Dealers;

namespace Khadra.Application.Dealers.Dtos;

/// <summary>
/// A dealer as its own owner or staff see it.
///
/// `CanTrade` is included even though it is derivable from the status, because it is the question
/// every client actually asks and the domain already answers it (approved AND not suspended AND not
/// deleted). Letting a client recompute that from a status string is how the three conditions get
/// forgotten one at a time.
/// </summary>
public sealed record DealerProfileDto(
    Guid DealerId,
    string BusinessName,
    string CommercialRegistrationNumber,
    string VerificationStatus,
    string? ReviewNote,
    DateTimeOffset SubmittedAt,
    DateTimeOffset ReviewDueAt,
    bool CanTrade,
    bool IsSuspended,
    IReadOnlyList<string> SubmittedDocuments,
    IReadOnlyList<string> MissingDocuments)
{
    public static DealerProfileDto From(Dealer dealer)
    {
        ArgumentNullException.ThrowIfNull(dealer);

        var held = dealer.Documents.Select(document => document.Type).ToList();
        return new DealerProfileDto(
            dealer.Id.Value,
            dealer.BusinessName.Value,
            dealer.CommercialRegistration.Value,
            dealer.VerificationStatus.Name,
            dealer.ReviewNote,
            dealer.SubmittedAt,
            dealer.ReviewDueAt,
            dealer.CanTrade,
            dealer.IsSuspended,
            [.. held.OrderBy(type => type.Id).Select(type => type.Name)],
            [.. DealerDocumentType.Required.Where(required => !held.Contains(required)).Select(type => type.Name)]);
    }
}
