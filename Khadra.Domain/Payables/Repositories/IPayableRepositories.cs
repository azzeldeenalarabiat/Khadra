using Khadra.Domain.Common;

namespace Khadra.Domain.Payables.Repositories;

/// <summary>The recorded payables. Every read loads a payable with its lines.</summary>
public interface IOfficePayableRepository
{
    Task<OfficePayable?> GetAsync(Id id, CancellationToken cancellationToken = default);

    /// <summary>A booking's payable: at most one, which the database enforces.</summary>
    Task<OfficePayable?> GetByBookingAsync(Id bookingId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The office's OPEN payables in one currency and one kind of money, oldest first: what a settlement may close
    /// once the held and blocked ones are left out.
    /// </summary>
    Task<IReadOnlyList<OfficePayable>> ListOpenForOfficeAsync(
        Id dealerId,
        string currency,
        string provider,
        CancellationToken cancellationToken = default);

    /// <summary>The payables a settlement closed and still names: what a void opens again.</summary>
    Task<IReadOnlyList<OfficePayable>> ListClosedByAsync(Id settlementId, CancellationToken cancellationToken = default);

    void Add(OfficePayable payable);
}

/// <summary>The recorded settlements and their voids.</summary>
public interface IOfficeSettlementRepository
{
    /// <summary>A settlement with its lines.</summary>
    Task<OfficeSettlement?> GetAsync(Id id, CancellationToken cancellationToken = default);

    Task<bool> IsVoidedAsync(Id settlementId, CancellationToken cancellationToken = default);

    void Add(OfficeSettlement settlement);

    void AddVoid(OfficeSettlementVoid voided);
}

/// <summary>The holds on bookings' payables.</summary>
public interface IOfficePayableHoldRepository
{
    Task<OfficePayableHold?> GetAsync(Id id, CancellationToken cancellationToken = default);

    /// <summary>The booking's open holds, whatever their reason.</summary>
    Task<IReadOnlyList<OfficePayableHold>> ListOpenForBookingAsync(Id bookingId, CancellationToken cancellationToken = default);

    /// <summary>The office's open holds: the payables they name are never due.</summary>
    Task<IReadOnlyList<OfficePayableHold>> ListOpenForOfficeAsync(Id dealerId, CancellationToken cancellationToken = default);

    void Add(OfficePayableHold hold);
}
