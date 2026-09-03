using Khadra.Domain.Common;

namespace Khadra.Domain.Dealers.Repositories;

public interface IDealerRepository
{
    // Loads the dealer with its employees and documents: they are part of the aggregate.
    Task<Dealer?> GetByIdAsync(Id id, CancellationToken cancellationToken = default);

    Task<Dealer?> GetByOwnerUserIdAsync(Id ownerUserId, CancellationToken cancellationToken = default);

    // The dealer a given employee works for, used to scope every dealer-staff request.
    Task<Dealer?> GetByStaffUserIdAsync(Id userId, CancellationToken cancellationToken = default);

    Task<bool> ExistsForOwnerAsync(Id ownerUserId, CancellationToken cancellationToken = default);

    Task<bool> CommercialRegistrationExistsAsync(
        CommercialRegistrationNumber number,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Dealer>> ListAwaitingReviewAsync(CancellationToken cancellationToken = default);

    Task AddAsync(Dealer dealer, CancellationToken cancellationToken = default);
}
