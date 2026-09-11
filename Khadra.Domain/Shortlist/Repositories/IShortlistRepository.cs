using Khadra.Domain.Common;

namespace Khadra.Domain.Shortlist.Repositories;

public interface IShortlistRepository
{
    /// <summary>
    /// This customer's list, with its entries loaded, or null if they have never saved a car.
    /// </summary>
    /// <remarks>
    /// Null is the ordinary answer for most accounts. The list is created on the first save rather
    /// than at registration, so a row per account for a feature most of them will not use never
    /// exists.
    /// </remarks>
    Task<CustomerShortlist?> GetAsync(Id customerId, CancellationToken cancellationToken = default);

    void Add(CustomerShortlist shortlist);

    /// <summary>
    /// Which of these cars this customer has already saved.
    /// </summary>
    /// <remarks>
    /// For the catalogue screens, which need a heart per card and must not load the whole aggregate
    /// to draw them. Answers only ids the caller already named, so it cannot be used to read a list
    /// it was not shown — and the empty set is the answer for a customer with no list at all.
    /// </remarks>
    Task<IReadOnlySet<Id>> SavedAmongAsync(
        Id customerId,
        IReadOnlyCollection<Id> vehicleIds,
        CancellationToken cancellationToken = default);
}
