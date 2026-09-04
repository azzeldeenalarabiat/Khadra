using Khadra.Application.Common;

namespace Khadra.Application.Dealers.ReadModels;

/// <summary>
/// One row of the Admin's dealer list.
///
/// Flat and pre-shaped for the screen: the list renders 20 rows at a time and must not load 20 whole
/// aggregates with their employees and documents to show a name and a status.
/// </summary>
public sealed record DealerListItem(
    // A plain guid: this record is serialised straight to the client, and Id is a struct whose
    // Value/IsEmpty members would otherwise leak into the JSON.
    Guid DealerId,
    string BusinessName,
    string CommercialRegistrationNumber,
    string VerificationStatus,
    bool IsSuspended,
    bool CanTrade,
    DateTimeOffset SubmittedAt,
    DateTimeOffset ReviewDueAt,
    DateTimeOffset CreatedAt,
    int DocumentCount,
    // How many DocumentCount is measured against. It is the same number on every row, and it is on
    // the row anyway: the list column reads "2 of 3", and a denominator the client supplies from
    // memory is one that goes stale silently the day a fourth document becomes required.
    int RequiredDocumentCount,
    int EmployeeCount,
    // Cars still listed, deleted ones excluded. Counted across every status, not just the published
    // ones: a dealer with eleven cars in draft has a fleet, and an admin reading the list wants to
    // know that rather than seeing a zero that means "nothing published yet".
    int CarCount,
    // Null means "no reviews yet", which is not the same as a rating of zero and must not render as
    // one. The Reviews context has no persistence at all yet (see docs/pre-launch-checklist.md), so
    // today this is always null -- the shape is here so the column stops lying the moment it exists.
    decimal? AverageRating,
    int ReviewCount);

/// <summary>Which slice of the dealer list the Admin is looking at.</summary>
public sealed record DealerListFilter(string? Status, bool? SuspendedOnly, string? Search);

public interface IDealerAdminReader
{
    Task<PagedResult<DealerListItem>> ListAsync(
        DealerListFilter filter,
        PageRequest page,
        CancellationToken cancellationToken = default);
}
