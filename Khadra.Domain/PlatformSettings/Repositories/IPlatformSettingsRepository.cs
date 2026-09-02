using Khadra.Domain.Common;

namespace Khadra.Domain.PlatformSettings.Repositories;

public interface IBusinessRuleSettingsRepository
{
    // There is exactly one settings row. Null means the platform has not been seeded yet.
    Task<BusinessRuleSettings?> GetCurrentAsync(CancellationToken cancellationToken = default);

    Task AddAsync(BusinessRuleSettings settings, CancellationToken cancellationToken = default);
}

public interface ICarTypeRepository
{
    Task<CarType?> GetByIdAsync(Id id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CarType>> ListAsync(bool activeOnly, CancellationToken cancellationToken = default);

    Task AddAsync(CarType carType, CancellationToken cancellationToken = default);
}

public interface ICityRepository
{
    Task<City?> GetByIdAsync(Id id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<City>> ListAsync(bool activeOnly, CancellationToken cancellationToken = default);

    Task AddAsync(City city, CancellationToken cancellationToken = default);
}
