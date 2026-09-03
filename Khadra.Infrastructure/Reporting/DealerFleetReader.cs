using Khadra.Application.Fleet.ReadModels;
using Khadra.Domain.Common;
using Khadra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Infrastructure.Reporting;

internal sealed class DealerFleetReader(KhadraDbContext context) : IDealerFleetReader
{
    // The vehicles' own soft-delete filter keeps removed listings out.
    public async Task<IReadOnlyList<FleetVehicleSummary>> SummaryAsync(Id dealerId, CancellationToken cancellationToken = default) =>
        await context.Vehicles
            .Where(vehicle => vehicle.DealerId == dealerId)
            .OrderBy(vehicle => vehicle.Details.Make)
            .ThenBy(vehicle => vehicle.Details.Model)
            .Select(vehicle => new FleetVehicleSummary(
                vehicle.Id.Value,
                vehicle.Details.Make,
                vehicle.Details.Model,
                vehicle.Details.Year,
                vehicle.PlateNumber.Value,
                vehicle.Status.Name,
                vehicle.IsDeliveryEligible,
                vehicle.DailyRate.Amount,
                vehicle.DailyRate.CurrencyCode))
            .ToListAsync(cancellationToken);
}
