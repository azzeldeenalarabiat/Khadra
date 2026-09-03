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
public sealed record DisputeDto(
    Guid TicketId,
    Guid BookingId,
    string Status,
    bool IsLive,
    string OpenedByParty,
    Guid OpenedByUserId,
    string OpenedByName,
    string Reason,
    DateTimeOffset OpenedAt,
    DateTimeOffset SlaDeadline,
    bool IsOverdue,
    Guid? AssignedAdminId,
    string? AssignedAdminName,
    DateTimeOffset? ClosedAt,
    IReadOnlyList<DisputeStatementDto> Statements,
    DisputeResolutionDto? Resolution,
    // The basis any resolution must split, taken from the same helper the resolve handler validates
    // against, so the workspace and the rule can never disagree about what is actually held.
    MoneyDto DepositHeld,
    BookingDto Booking);

public sealed record DisputeStatementDto(
    Guid StatementId,
    string Party,
    Guid AuthorUserId,
    string AuthorName,
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
    DateTimeOffset ResolvedAt)
{
    public static DisputeResolutionDto From(DisputeResolution resolution, string resolvedByName)
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
            resolution.ResolvedAt);
    }
}

/// <summary>What the party gets back from a successful upload request: where to PUT, and the key to quote.</summary>
public sealed record EvidenceUploadDto(string UploadUrl, string StorageKey, DateTimeOffset ExpiresAt);
