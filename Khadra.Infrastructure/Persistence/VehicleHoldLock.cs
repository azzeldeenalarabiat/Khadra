using Khadra.Application.Common.Ports;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Infrastructure.Persistence;

/// <summary>
/// A Postgres transaction-scoped advisory lock, keyed on the vehicle.
/// </summary>
/// <remarks>
/// <c>pg_advisory_xact_lock</c> rather than the session-scoped <c>pg_advisory_lock</c>: the
/// transaction-scoped one is released by COMMIT or ROLLBACK with no unlock call, so no failure path
/// can leave a lock behind on a connection that then goes back into the pool and poisons the next
/// request for that car.
///
/// The key is a bigint, so the vehicle's id is hashed with <c>hashtextextended</c>. A collision
/// between two vehicles costs one of them a short wait and nothing else — the lock is an
/// optimisation of TRUTHFULNESS, not the correctness floor. That floor is
/// <c>bookings_one_hold_per_vehicle</c>, which is still what makes double-booking impossible.
///
/// On any other provider this does nothing, and that is deliberate rather than a gap: the
/// persistence suite runs on SQLite, which is single-writer anyway, so there is no race to serialise
/// and no advisory-lock function to call.
/// </remarks>
internal sealed class VehicleHoldLock(KhadraDbContext context) : IVehicleHoldLock
{
    public async Task AcquireAsync(Guid vehicleId, CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql())
            return;

        // Parameterised, so the id never reaches the server as text in a statement.
        await context.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({vehicleId.ToString()}, 0))",
            cancellationToken);
    }
}
