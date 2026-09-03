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
    int EmployeeCount);

/// <summary>Which slice of the dealer list the Admin is looking at.</summary>
public sealed record DealerListFilter(string? Status, bool? SuspendedOnly, string? Search);

public interface IDealerAdminReader
{
    Task<PagedResult<DealerListItem>> ListAsync(
        DealerListFilter filter,
        PageRequest page,
        CancellationToken cancellationToken = default);
}
