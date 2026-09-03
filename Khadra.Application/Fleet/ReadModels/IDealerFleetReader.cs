using Khadra.Domain.Common;

namespace Khadra.Application.Fleet.ReadModels;

/// <summary>A car as the dealer's dashboard and reports name it.</summary>
public sealed record FleetVehicleSummary(
    Guid VehicleId,
    string Make,
    string Model,
    int Year,
    string PlateNumber,
    // The stored status: Draft, Active, Hidden, Maintenance. "Booked" is never stored -- it is a
    // fact about bookings, and the console derives it from IDealerBookingReader.HeldVehicleIdsAsync.
    string Status,
    bool IsDeliveryEligible,
    decimal DailyRate,
    string Currency);

public interface IDealerFleetReader
{
    Task<IReadOnlyList<FleetVehicleSummary>> SummaryAsync(Id dealerId, CancellationToken cancellationToken = default);
}
