using Khadra.Domain.Common;

namespace Khadra.Domain.Fleet.Repositories;

public interface IVehicleRepository
{
    Task<Vehicle?> GetByIdAsync(Id id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Vehicle>> ListByDealerAsync(Id dealerId, CancellationToken cancellationToken = default);

    // Plate numbers are unique across the platform: the same car cannot be listed by two dealers.
    Task<bool> PlateNumberExistsAsync(PlateNumber plateNumber, CancellationToken cancellationToken = default);

    Task<int> CountActiveByDealerAsync(Id dealerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// How many of this dealership's LISTED cars are not offered for delivery.
    /// </summary>
    /// <remarks>
    /// Active only. A draft or hidden car is not advertising anything, so it cannot be part of the
    /// mismatch between "this gallery delivers" and "none of its cars do" -- and offering to change
    /// a draft would be editing a listing the owner has not finished writing.
    /// </remarks>
    Task<int> CountPublishedNotDeliveryEligibleAsync(
        Id dealerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Those same cars, loaded so they can be changed.
    /// </summary>
    Task<IReadOnlyList<Vehicle>> ListPublishedNotDeliveryEligibleAsync(
        Id dealerId,
        CancellationToken cancellationToken = default);

    Task AddAsync(Vehicle vehicle, CancellationToken cancellationToken = default);
}
