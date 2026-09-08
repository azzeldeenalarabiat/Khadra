using Khadra.Domain.Common;
using Khadra.Domain.Fleet;
using Khadra.Domain.Fleet.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Infrastructure.Persistence.Repositories;

internal sealed class VehicleRepository(KhadraDbContext context) : IVehicleRepository
{
    public Task<Vehicle?> GetByIdAsync(Id id, CancellationToken cancellationToken = default) =>
        context.Vehicles
            .Include(vehicle => vehicle.Images)
            .SingleOrDefaultAsync(vehicle => vehicle.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Vehicle>> ListByDealerAsync(
        Id dealerId,
        CancellationToken cancellationToken = default) =>
        await context.Vehicles
            .Include(vehicle => vehicle.Images)
            .Where(vehicle => vehicle.DealerId == dealerId)
            .OrderByDescending(vehicle => vehicle.CreatedAt)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Uniqueness holds across soft-deleted rows: a dealer who deletes a listing has not given the
    /// plate back, and letting another dealer claim it would let one car appear twice on the platform.
    /// </summary>
    public Task<bool> PlateNumberExistsAsync(
        PlateNumber plateNumber,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plateNumber);
        return context.Vehicles
            .IgnoreQueryFilters()
            .AnyAsync(vehicle => vehicle.PlateNumber == plateNumber, cancellationToken);
    }

    public Task<int> CountActiveByDealerAsync(Id dealerId, CancellationToken cancellationToken = default)
    {
        var active = VehicleStatus.Active;
        return context.Vehicles.CountAsync(
            vehicle => vehicle.DealerId == dealerId && vehicle.Status == active, cancellationToken);
    }

    public Task<int> CountPublishedNotDeliveryEligibleAsync(
        Id dealerId,
        CancellationToken cancellationToken = default)
    {
        var active = VehicleStatus.Active;
        return context.Vehicles.CountAsync(
            vehicle =>
                vehicle.DealerId == dealerId &&
                vehicle.Status == active &&
                !vehicle.IsDeliveryEligible,
            cancellationToken);
    }

    public async Task<IReadOnlyList<Vehicle>> ListPublishedNotDeliveryEligibleAsync(
        Id dealerId,
        CancellationToken cancellationToken = default)
    {
        var active = VehicleStatus.Active;
        // Tracked, not AsNoTracking: the caller is about to change every one of them.
        return await context.Vehicles
            .Where(vehicle =>
                vehicle.DealerId == dealerId &&
                vehicle.Status == active &&
                !vehicle.IsDeliveryEligible)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(Vehicle vehicle, CancellationToken cancellationToken = default) =>
        await context.Vehicles.AddAsync(vehicle, cancellationToken);
}
