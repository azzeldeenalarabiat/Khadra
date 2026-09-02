using Khadra.Domain.Common;

namespace Khadra.Domain.Fleet.Repositories;

public interface IVehicleRepository
{
    Task<Vehicle?> GetByIdAsync(Id id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Vehicle>> ListByDealerAsync(Id dealerId, CancellationToken cancellationToken = default);

    // Plate numbers are unique across the platform: the same car cannot be listed by two dealers.
    Task<bool> PlateNumberExistsAsync(PlateNumber plateNumber, CancellationToken cancellationToken = default);

    Task<int> CountActiveByDealerAsync(Id dealerId, CancellationToken cancellationToken = default);

    Task AddAsync(Vehicle vehicle, CancellationToken cancellationToken = default);
}
