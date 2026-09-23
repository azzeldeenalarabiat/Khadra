using Khadra.Domain.Bookings;
using Khadra.Domain.Bookings.Repositories;
using Khadra.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Infrastructure.Persistence.Repositories;

internal sealed class HandoverCodeRepository(KhadraDbContext context) : IHandoverCodeRepository
{
    public async Task AddAsync(HandoverCode code, CancellationToken cancellationToken = default) =>
        await context.HandoverCodes.AddAsync(code, cancellationToken);

    public Task<HandoverCode?> GetCurrentAsync(Id bookingId, HandoverType type, CancellationToken cancellationToken = default) =>
        context.HandoverCodes
            .Where(code => code.BookingId == bookingId && code.Type == type && code.SupersededAt == null)
            .OrderByDescending(code => code.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<HandoverCode>> ListCurrentAsync(
        Id bookingId,
        HandoverType type,
        CancellationToken cancellationToken = default) =>
        await context.HandoverCodes
            .Where(code => code.BookingId == bookingId && code.Type == type && code.SupersededAt == null)
            .ToListAsync(cancellationToken);
}
