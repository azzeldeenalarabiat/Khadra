using Khadra.Domain.Common;
using Khadra.Domain.Payments;
using Khadra.Domain.Payments.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Infrastructure.Persistence.Repositories;

internal sealed class PaymentRepository(KhadraDbContext context) : IPaymentRepository
{
    public Task<Payment?> GetByIdAsync(Id id, CancellationToken cancellationToken = default) =>
        WithRefunds().FirstOrDefaultAsync(payment => payment.Id == id, cancellationToken);

    public Task<Payment?> GetByProviderReferenceAsync(
        string provider,
        string providerReference,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerReference);

        // BOTH halves. A reference is only unique within a provider, and a platform that has changed
        // provider still holds rows from the old one.
        return WithRefunds().FirstOrDefaultAsync(
            payment => payment.Provider == provider && payment.ProviderReference == providerReference,
            cancellationToken);
    }

    public Task<Payment?> GetLiveForBookingAsync(Id bookingId, CancellationToken cancellationToken = default) =>
        WithRefunds()
            .Where(payment => payment.BookingId == bookingId)
            .Where(payment => payment.Status == PaymentStatus.Initiated || payment.Status == PaymentStatus.Pending)
            // At most one can exist -- a partial unique index says so -- but the order makes the
            // answer deterministic if that index is ever dropped, rather than silently arbitrary.
            .OrderByDescending(payment => payment.CreatedAt)
            .ThenByDescending(payment => payment.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<Payment?> GetAppliedForBookingAsync(Id bookingId, CancellationToken cancellationToken = default) =>
        WithRefunds().FirstOrDefaultAsync(
            payment => payment.BookingId == bookingId && payment.Status == PaymentStatus.Applied,
            cancellationToken);

    public async Task<IReadOnlyList<Payment>> ListForBookingAsync(
        Id bookingId,
        CancellationToken cancellationToken = default) =>
        await WithRefunds()
            .Where(payment => payment.BookingId == bookingId)
            .OrderByDescending(payment => payment.CreatedAt)
            .ThenByDescending(payment => payment.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Payment>> ListStaleLiveAsync(
        DateTimeOffset expiredBefore,
        CancellationToken cancellationToken = default) =>
        await WithRefunds()
            .Where(payment => payment.Status == PaymentStatus.Initiated || payment.Status == PaymentStatus.Pending)
            .Where(payment => payment.ExpiresAt < expiredBefore)
            .OrderBy(payment => payment.ExpiresAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Payment>> ListWithOutstandingRefundsAsync(
        CancellationToken cancellationToken = default) =>
        await WithRefunds()
            .Where(payment => payment.Refunds.Any(refund =>
                refund.Status == RefundStatus.Requested || refund.Status == RefundStatus.Failed))
            .OrderBy(payment => payment.CreatedAt)
            .ToListAsync(cancellationToken);

    public void Add(Payment payment) => context.Payments.Add(payment);

    // Refunds are part of the aggregate: loading a payment without them would let RefundedTotal read
    // zero and a second refund pass a guard it should have failed.
    private IQueryable<Payment> WithRefunds() => context.Payments.Include(payment => payment.Refunds);
}

internal sealed class ProviderEventReceiptRepository(KhadraDbContext context) : IProviderEventReceiptRepository
{
    public void Add(ProviderEventReceipt receipt) => context.ProviderEventReceipts.Add(receipt);

    public Task<bool> HasSeenAsync(
        string provider,
        string providerEventId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerEventId);

        return context.ProviderEventReceipts.AnyAsync(
            receipt => receipt.Provider == provider && receipt.ProviderEventId == providerEventId,
            cancellationToken);
    }
}
