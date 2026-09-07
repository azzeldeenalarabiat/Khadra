using Khadra.Domain.Common;
using Khadra.Domain.PlatformSettings;
using Khadra.Domain.PlatformSettings.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Infrastructure.Persistence.Repositories;

// Ordered by the display order an administrator chose, then by name so a tie is settled here rather
// than by the database.

internal sealed class CarTypeRepository(KhadraDbContext context) : ICarTypeRepository
{
    public Task<CarType?> GetByIdAsync(Id id, CancellationToken cancellationToken = default) =>
        context.CarTypes.SingleOrDefaultAsync(carType => carType.Id == id, cancellationToken);

    public async Task<IReadOnlyList<CarType>> ListAsync(bool activeOnly, CancellationToken cancellationToken = default) =>
        await context.CarTypes
            .Where(carType => !activeOnly || carType.IsActive)
            .OrderBy(carType => carType.DisplayOrder)
            .ThenBy(carType => carType.NameEn)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(CarType carType, CancellationToken cancellationToken = default) =>
        await context.CarTypes.AddAsync(carType, cancellationToken);
}

internal sealed class CityRepository(KhadraDbContext context) : ICityRepository
{
    public Task<City?> GetByIdAsync(Id id, CancellationToken cancellationToken = default) =>
        context.Cities.SingleOrDefaultAsync(city => city.Id == id, cancellationToken);

    public async Task<IReadOnlyList<City>> ListAsync(bool activeOnly, CancellationToken cancellationToken = default) =>
        await context.Cities
            .Where(city => !activeOnly || city.IsActive)
            .OrderBy(city => city.DisplayOrder)
            .ThenBy(city => city.NameEn)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(City city, CancellationToken cancellationToken = default) =>
        await context.Cities.AddAsync(city, cancellationToken);
}
