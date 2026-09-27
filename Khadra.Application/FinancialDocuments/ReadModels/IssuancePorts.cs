using Khadra.Application.FinancialDocuments.Composition;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.Disputes;
using Khadra.Domain.FinancialDocuments;
using Khadra.Domain.Payments;

namespace Khadra.Application.FinancialDocuments.ReadModels;

/// <summary>
/// How financial documents are issued, from configuration (payments Phase 5). None of it is a business
/// rule: nothing here changes a figure on any document, only when and whether one is issued.
/// </summary>
public interface IFinancialDocumentSettings
{
    /// <summary>
    /// Khadra's legal identity, or null while it is not configured. Never partial: startup refuses a
    /// half-configured issuer, and no document is issued with placeholder or incomplete issuer
    /// information (owner, 2026-09-27).
    /// </summary>
    DocumentIssuer? Issuer { get; }

    /// <summary>The most documents one settlement pass issues or holds; the rest wait for the next pass.</summary>
    int MaxDocumentsPerPass { get; }

    /// <summary>The first wait after a family is put on hold; each further failure doubles it.</summary>
    TimeSpan RetryInitialDelay { get; }

    /// <summary>The longest wait between two attempts at a family on hold.</summary>
    TimeSpan RetryMaxDelay { get; }

    /// <summary>
    /// How long after a statement is issued its booking is still looked at again: a handler takes its time
    /// before it commits, so a fact can arrive carrying an instant older than a statement that did not see it.
    /// The checkpoint fingerprint decides whether it really is new.
    /// </summary>
    TimeSpan LateCommitMargin { get; }
}

/// <summary>A booking's money as it is STORED: what documents are composed from.</summary>
/// <param name="Payments">Every payment on the booking, with its refunds.</param>
/// <param name="ResolvedTickets">The booking's resolved dispute tickets.</param>
/// <param name="HasLiveDispute">Whether a dispute on the booking is open.</param>
public sealed record BookingMoneyFacts(
    Booking Booking,
    IReadOnlyList<Payment> Payments,
    IReadOnlyList<DisputeTicket> ResolvedTickets,
    bool HasLiveDispute);

/// <summary>
/// Reads what documents are made of (payments Phase 5). The booking comes AS STORED — never settled against
/// the clock in memory — because a record is composed from committed facts only: a lapse that exists only in
/// memory carries the current instant as its ending, which would change on every pass. The settlement pass
/// commits every lapse before it issues anything.
/// </summary>
public interface IFinancialDocumentFactsReader
{
    Task<BookingMoneyFacts?> BookingAsync(Id bookingId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The customer's name, the office and the car as they stand now, read past the soft-delete filter: a
    /// document names the office that received the money even after it has left the platform. Null when
    /// any of the three does not resolve at all.
    /// </summary>
    Task<DocumentParties?> PartiesAsync(Id customerId, Id dealerId, Id vehicleId, CancellationToken cancellationToken = default);
}

/// <summary>A document family that is owed a document.</summary>
public sealed record IssuanceCandidate(FinancialDocumentType Type, Id SubjectId, Id BookingId);

/// <summary>
/// Finds what is owed a document (payments Phase 5). The durable facts ARE the queue: nothing records that a
/// document is due, so nothing can be lost — a captured payment with no receipt row, a settled refund with
/// none, a booking whose checkpoints are newer than its latest statement.
/// </summary>
public interface IFinancialDocumentCandidateReader
{
    /// <summary>
    /// Payment receipts first, then refund receipts (only once their payment's receipt exists), then
    /// statements — each oldest money first, at most <paramref name="limit"/> in all. A family on hold waits
    /// for its next attempt, except that a hold for a missing issuer stops waiting once an issuer exists.
    /// </summary>
    Task<IReadOnlyList<IssuanceCandidate>> ListAsync(
        DateTimeOffset now,
        TimeSpan lateCommitMargin,
        bool issuerConfigured,
        int limit,
        CancellationToken cancellationToken = default);
}
