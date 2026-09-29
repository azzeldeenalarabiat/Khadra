using Khadra.Domain.Common;
using Khadra.Domain.Payables;
using Khadra.Domain.Payables.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Infrastructure.Persistence.Repositories;

/// <summary>The recorded payables (payments Phase 8). Every read brings the lines, which the figures are derived from.</summary>
internal sealed class OfficePayableRepository(KhadraDbContext context) : IOfficePayableRepository
{
    public Task<OfficePayable?> GetAsync(Id id, CancellationToken cancellationToken = default) =>
        WithLines().FirstOrDefaultAsync(payable => payable.Id == id, cancellationToken);

    public Task<OfficePayable?> GetByBookingAsync(Id bookingId, CancellationToken cancellationToken = default) =>
        WithLines().FirstOrDefaultAsync(payable => payable.BookingId == bookingId, cancellationToken);

    public async Task<IReadOnlyList<OfficePayable>> ListOpenForOfficeAsync(
        Id dealerId,
        string currency,
        string provider,
        CancellationToken cancellationToken = default) =>
        await WithLines()
            .Where(payable =>
                payable.DealerId == dealerId &&
                payable.Currency == currency &&
                payable.Provider == provider &&
                payable.SettlementId == null)
            .OrderBy(payable => payable.FinalAt)
            .ThenBy(payable => payable.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<OfficePayable>> ListClosedByAsync(Id settlementId, CancellationToken cancellationToken = default) =>
        await WithLines()
            .Where(payable => payable.SettlementId == settlementId)
            .ToListAsync(cancellationToken);

    public void Add(OfficePayable payable) => context.OfficePayables.Add(payable);

    private IQueryable<OfficePayable> WithLines() => context.OfficePayables.Include(payable => payable.Lines);
}

/// <summary>Settlements and their voids (payments Phase 8). Append-only: nothing here updates or removes.</summary>
internal sealed class OfficeSettlementRepository(KhadraDbContext context) : IOfficeSettlementRepository
{
    public Task<OfficeSettlement?> GetAsync(Id id, CancellationToken cancellationToken = default) =>
        context.OfficeSettlements
            .Include(settlement => settlement.Lines)
            .FirstOrDefaultAsync(settlement => settlement.Id == id, cancellationToken);

    public Task<bool> IsVoidedAsync(Id settlementId, CancellationToken cancellationToken = default) =>
        context.OfficeSettlementVoids.AnyAsync(voided => voided.Id == settlementId, cancellationToken);

    public void Add(OfficeSettlement settlement) => context.OfficeSettlements.Add(settlement);

    public void AddVoid(OfficeSettlementVoid voided) => context.OfficeSettlementVoids.Add(voided);
}

/// <summary>Holds on bookings' payables (payments Phase 8).</summary>
internal sealed class OfficePayableHoldRepository(KhadraDbContext context) : IOfficePayableHoldRepository
{
    public Task<OfficePayableHold?> GetAsync(Id id, CancellationToken cancellationToken = default) =>
        context.OfficePayableHolds.FirstOrDefaultAsync(hold => hold.Id == id, cancellationToken);

    public async Task<IReadOnlyList<OfficePayableHold>> ListOpenForBookingAsync(Id bookingId, CancellationToken cancellationToken = default) =>
        await context.OfficePayableHolds
            .Where(hold => hold.BookingId == bookingId && hold.ReleasedAt == null)
            .OrderBy(hold => hold.OpenedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<OfficePayableHold>> ListOpenForOfficeAsync(Id dealerId, CancellationToken cancellationToken = default) =>
        await context.OfficePayableHolds
            .Where(hold => hold.DealerId == dealerId && hold.ReleasedAt == null)
            .ToListAsync(cancellationToken);

    public void Add(OfficePayableHold hold) => context.OfficePayableHolds.Add(hold);
}
