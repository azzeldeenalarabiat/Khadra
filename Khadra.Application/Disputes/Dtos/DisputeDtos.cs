using Khadra.Application.Bookings.Dtos;
using Khadra.Application.Common.Dtos;
using Khadra.Domain.Disputes;

namespace Khadra.Application.Disputes.Dtos;

/// <summary>
/// One dispute in full, for whoever is allowed to see it: both parties and the Admin.
///
/// Carries the whole booking, because a dispute is unreadable without it -- the reason references the
/// car, the resolution is a split of the booking's own deposit, and the penalty range the Admin picks
/// inside was fixed by the booking when the event happened.
/// </summary>
/// <remarks>
/// Every person named here is a name AND a fact. The customer app receives this DTO, so each name
/// stays a string -- an English stand-in when the account no longer resolves -- and each
/// <c>...AccountClosed</c> flag says when it is one. A client that words the case in its reader's
/// language reads the flag and never shows the stand-in.
/// </remarks>
public sealed record DisputeDto(
    Guid TicketId,
    Guid BookingId,
    string Status,
    bool IsLive,
    string OpenedByParty,
    Guid OpenedByUserId,
    string OpenedByName,
    /// <summary>True exactly when the opener's account no longer resolves, so OpenedByName is the stand-in.</summary>
    bool OpenedByAccountClosed,
    string Reason,
    DateTimeOffset OpenedAt,
    DateTimeOffset SlaDeadline,
    bool IsOverdue,
    Guid? AssignedAdminId,
    /// <summary>Null exactly when nobody holds the ticket.</summary>
    string? AssignedAdminName,
    /// <summary>True exactly when the ticket is held by an account that no longer resolves.</summary>
    bool AssignedAdminAccountClosed,
    DateTimeOffset? ClosedAt,
    IReadOnlyList<DisputeStatementDto> Statements,
    DisputeResolutionDto? Resolution,
    // What THIS ticket can split: for a live or withdrawn ticket, the deposit less what the booking's
    // earlier disputes decided (item 169); for a resolved one, the basis it was decided against. The
    // same calculator the resolve handler validates against, so the form and the rule never disagree.
    MoneyDto DepositHeld,
    BookingDto Booking,
    /// <summary>
    /// The deposit the platform held for disputes before any was resolved (zero once the whole payment
    /// went back or the window released it). Added 2026-09-26, last.
    /// </summary>
    MoneyDto? DepositOnBooking = null,
    /// <summary>
    /// What the booking's EARLIER resolved disputes already decided, so a later ticket's smaller basis
    /// is explained rather than a mystery. Zero on a first dispute. Added 2026-09-26, last.
    /// </summary>
    MoneyDto? DecidedByEarlierTickets = null);

public sealed record DisputeStatementDto(
    Guid StatementId,
    string Party,
    Guid AuthorUserId,
    string AuthorName,
    /// <summary>True exactly when the author's account no longer resolves, so AuthorName is the stand-in.</summary>
    bool AuthorAccountClosed,
    string Body,
    DateTimeOffset CreatedAt,
    // Freshly signed per request, never stored: a signed URL is a credential (spec 7).
    IReadOnlyList<EvidenceLinkDto> Evidence);

public sealed record EvidenceLinkDto(string FileName, string Url, DateTimeOffset ExpiresAt);

/// <summary>
/// The Admin's decision, as money. Recorded, not executed: until the Payments context ships, nothing
/// here moves funds, and the console says so on every resolution.
/// </summary>
public sealed record DisputeResolutionDto(
    MoneyDto DepositHeld,
    MoneyDto RefundToCustomer,
    MoneyDto RetainedByPlatform,
    MoneyDto TransferredToDealer,
    MoneyDto? DealerCharge,
    bool WaivesEverything,
    string Note,
    Guid ResolvedByAdminId,
    string ResolvedByName,
    /// <summary>True exactly when the resolving administrator's account no longer resolves.</summary>
    bool ResolvedByAccountClosed,
    DateTimeOffset ResolvedAt)
{
    public static DisputeResolutionDto From(
        DisputeResolution resolution,
        string resolvedByName,
        bool resolvedByAccountClosed)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        return new DisputeResolutionDto(
            MoneyDto.From(resolution.Deposit.DepositHeld),
            MoneyDto.From(resolution.Deposit.RefundToCustomer),
            MoneyDto.From(resolution.Deposit.RetainedByPlatform),
            MoneyDto.From(resolution.Deposit.TransferredToDealer),
            MoneyDto.FromOptional(resolution.DealerCharge),
            resolution.WaivesEverything,
            resolution.Note,
            resolution.ResolvedByAdminId.Value,
            resolvedByName,
            resolvedByAccountClosed,
            resolution.ResolvedAt);
    }
}

/// <summary>What the party gets back from a successful upload request: where to PUT, and the key to quote.</summary>
public sealed record EvidenceUploadDto(string UploadUrl, string StorageKey, DateTimeOffset ExpiresAt);
