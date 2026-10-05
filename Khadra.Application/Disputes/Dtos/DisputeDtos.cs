using Khadra.Application.Bookings.Dtos;
using Khadra.Application.Common.Dtos;
using Khadra.Domain.Disputes;

namespace Khadra.Application.Disputes.Dtos;

/// <summary>
/// One dispute in full, for whoever is allowed to see it: both parties and the Admin. The money is the
/// exception: the rental office's copy carries only its own part of a decision
/// (<see cref="DisputeResolutionDto.ForDealer"/>).
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
    MoneyDto? DecidedByEarlierTickets = null,
    /// <summary>
    /// What the booking's earlier decisions already charged the office, so the form can bound a further
    /// charge by what is left of the assessed range (E2E F34). Administrators only: null on the parties'
    /// copies, because a customer is never shown the office's charges (owner decision 3). Added 2026-10-05.
    /// </summary>
    MoneyDto? ChargedToDealerEarlier = null,
    /// <summary>
    /// <see cref="DisputeSlaStates"/>: Closed, OnTime, AtRisk or Overdue, for the administrator's SLA panel
    /// (E2E F42). Administrators only: null on the parties' copies. Added 2026-10-05, last.
    /// </summary>
    string? SlaState = null);

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
/// The Admin's decision, as money. The customer's share is refunded automatically (payments Phase 3);
/// what the platform keeps and what goes to the office are settled by hand until a payout rail exists.
/// </summary>
public sealed record DisputeResolutionDto(
    MoneyDto DepositHeld,
    /// <summary>The customer's share. Null on the rental office's copy (<see cref="ForDealer"/>).</summary>
    MoneyDto? RefundToCustomer,
    /// <summary>The platform's share. Null on the rental office's copy (<see cref="ForDealer"/>).</summary>
    MoneyDto? RetainedByPlatform,
    MoneyDto TransferredToDealer,
    MoneyDto? DealerCharge,
    /// <summary>
    /// True when the customer received the whole basis and nobody was charged. Null on the rental
    /// office's copy: it is read from the platform's share, so it would tell the office the customer's.
    /// </summary>
    bool? WaivesEverything,
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

    /// <summary>
    /// The rental office's copy (owner decision 3, 2026-09-26; pre-launch item 151): the basis the decision
    /// split, the office's own share and any charge assessed to it — never the customer's refund or the
    /// platform's share, nor the waiver flag, which is read from them. The customer's copy is unchanged:
    /// it is the installed app's contract, and the owner deferred its half of item 151 (2026-09-27).
    /// </summary>
    public DisputeResolutionDto ForDealer() =>
        this with { RefundToCustomer = null, RetainedByPlatform = null, WaivesEverything = null };
}

/// <summary>What the party gets back from a successful upload request: where to PUT, and the key to quote.</summary>
public sealed record EvidenceUploadDto(string UploadUrl, string StorageKey, DateTimeOffset ExpiresAt);
