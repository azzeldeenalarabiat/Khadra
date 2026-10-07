using Khadra.Domain.Common;

namespace Khadra.Domain.Payments.Repositories;

public interface IPaymentRepository
{
    Task<Payment?> GetByIdAsync(Id id, CancellationToken cancellationToken = default);

    /// <summary>
    /// The payment a provider's event is talking about.
    /// </summary>
    /// <remarks>
    /// The reference is the ONLY thing that resolves a provider event to a booking. Whatever a
    /// provider echoes back in its metadata is caller-supplied data that has been round-tripped
    /// through a third party, and trusting a booking id from there would let anyone who can reach the
    /// webhook nominate which rental a capture confirms.
    /// </remarks>
    Task<Payment?> GetByProviderReferenceAsync(
        string provider,
        string providerReference,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The attempt that already took the capture this reference names, if any (Wave 4, B1).
    /// </summary>
    /// <remarks>
    /// Asked before anything else when a capture notice carries a reference: a capture another attempt already
    /// applied or orphaned is an incident, answered 2xx, never a second application. The unique index on
    /// (provider, provider_capture_reference) is the floor under this read, as the receipt's is under the replay
    /// check.
    /// </remarks>
    Task<Payment?> GetByCaptureReferenceAsync(
        string provider,
        string captureReference,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The attempt this booking is currently paying through, if there is one.
    /// </summary>
    /// <remarks>
    /// At most one can exist: a partial unique index over the live statuses says so. This is the
    /// friendly read; the index is the floor, and a lost race surfaces there rather than here.
    /// </remarks>
    Task<Payment?> GetLiveForBookingAsync(Id bookingId, CancellationToken cancellationToken = default);

    /// <summary>Every attempt on a booking, newest first. For the admin's view of a disputed rental.</summary>
    Task<IReadOnlyList<Payment>> ListForBookingAsync(Id bookingId, CancellationToken cancellationToken = default);

    /// <summary>Every attempt on several bookings, with their refunds, in ONE read: for batches that run the calculator per booking.</summary>
    Task<IReadOnlyList<Payment>> ListForBookingsAsync(IReadOnlyCollection<Id> bookingIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// The payment that actually holds this booking's deposit, or null if none ever did.
    /// </summary>
    /// <remarks>
    /// Exactly one payment per booking can reach <see cref="PaymentStatus.Applied"/>: the second
    /// capture finds the booking already carrying another payment's id and is orphaned. This is where
    /// a refund on a resolved dispute is attached.
    /// </remarks>
    Task<Payment?> GetAppliedForBookingAsync(Id bookingId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts whose session should be dead but whose row still says live.
    /// </summary>
    /// <remarks>
    /// A provider that never delivers its expiry notice would otherwise leave a booking with a live
    /// attempt it can never replace, so the customer cannot open a second checkout inside their own
    /// payment window. The grace keeps the sweep from racing an expiry event already in flight.
    /// </remarks>
    Task<IReadOnlyList<Payment>> ListStaleLiveAsync(DateTimeOffset expiredBefore, CancellationToken cancellationToken = default);

    /// <summary>Payments carrying a refund that has not reached the provider yet.</summary>
    Task<IReadOnlyList<Payment>> ListWithOutstandingRefundsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The payments with a refund the sweep may send at <paramref name="now"/>: owed, not with the provider, and
    /// never refused or past the wait its last refusal set (Wave 4, B4). Ids only, oldest payment first.
    /// </summary>
    /// <remarks>
    /// Ids, so the sweep loads and saves ONE payment at a time: a refund the webhook settled while the sweep held it
    /// then costs that payment's sends only, not the whole tick's. Only payments with a provider reference, the one
    /// thing a refund is sent against.
    /// </remarks>
    Task<IReadOnlyList<Id>> ListIdsWithRefundsDueAsync(DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>
    /// The safety net under the ending refunds (Phase 3): applied payments IN FULL whose booking ended
    /// before pickup with no refund recorded for the ending, and payments whose booking an
    /// administrator cancelled before pickup with no whole-payment refund. Normally empty; a row here
    /// is a way of ending a booking that forgot to record what it owes, or a booking ended before the
    /// rule existed.
    /// </summary>
    Task<IReadOnlyList<Payment>> ListEndedWithoutEndingRefundAsync(CancellationToken cancellationToken = default);

    void Add(Payment payment);
}

public interface IProviderEventReceiptRepository
{
    /// <summary>
    /// Stages the receipt. It is NOT a check: the caller must let the unique index refuse a
    /// duplicate, in the same transaction as the effect, or two concurrent deliveries both pass a
    /// read that happened before either wrote.
    /// </summary>
    void Add(ProviderEventReceipt receipt);

    /// <summary>
    /// Whether this delivery has already been recorded. A courtesy for the read-only path only --
    /// never the guard.
    /// </summary>
    Task<bool> HasSeenAsync(string provider, string providerEventId, CancellationToken cancellationToken = default);
}

/// <summary>Capture incidents waiting for, or dealt with by, a person (Wave 4, B1).</summary>
public interface IPaymentIncidentRepository
{
    /// <summary>Staged beside the receipt that raised it, in the same save.</summary>
    void Add(PaymentIncident incident);

    Task<PaymentIncident?> GetByIdAsync(Id id, CancellationToken cancellationToken = default);
}
