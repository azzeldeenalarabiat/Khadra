using Khadra.Application.Common.Dtos;
using Khadra.Application.Payables.Dtos;

namespace Khadra.Application.Disputes.Dtos;

/// <summary>
/// What a dispute decision WOULD do, before an administrator makes it (Wave 2 C1; E2E F37): the same checks as
/// resolving, and the rental office's money exactly as the office payables ledger would record it, from the one pure
/// office function the ledger itself uses. Nothing is written to produce it.
/// </summary>
/// <remarks>
/// <para>
/// It states what the records WILL say, never what money will DO, so it never claims:
/// <list type="bullet">
/// <item>that the office "will receive" anything: settlement is manual and can be held or voided;</item>
/// <item>that the customer "was refunded": the refund is requested, and the payment sweep sends it;</item>
/// <item>an exact recording time: <see cref="RecordedNotBefore"/> is a floor, not a promise;</item>
/// <item>finality before a cancellation's or a no-show's dispute window closes: another dispute may still be opened
/// until <see cref="FurtherDecisionsPossibleUntil"/>, and would change these figures.</item>
/// </list>
/// </para>
/// <para>
/// Administrators only. The office's commission is never shown to a customer, and the platform's share never to the
/// office.
/// </para>
/// </remarks>
/// <param name="StatusAfter">The booking's status once decided: <c>Completed</c> for a returned booking, else unchanged.</param>
/// <param name="OfficeState"><c>Final</c>, or <c>NotApplicable</c> when nothing was paid online.</param>
/// <param name="Outcome">The payable's outcome, as the payouts page names it; null when not applicable.</param>
/// <param name="Lines">The payable's lines, in the payouts page's shape. A line from this decision carries this ticket's id.</param>
/// <param name="RecordedNotBefore">The earliest the ledger can record it: the later of now and the final moment, plus its margin.</param>
/// <param name="FurtherDecisionsPossibleUntil">
/// When the booking's dispute window closes, for a cancellation or a no-show; null for a returned booking, which the
/// decision completes.
/// </param>
public sealed record ResolutionPreviewDto(
    string StatusAfter,
    string OfficeState,
    string? Outcome,
    IReadOnlyList<PayableLineDto> Lines,
    ResolutionPreviewCustomerDto Customer,
    ResolutionPreviewPlatformDto Platform,
    ResolutionPreviewOfficeDto Office,
    ResolutionPreviewEarlierDto? EarlierDecisions,
    DateTimeOffset RecordedNotBefore,
    DateTimeOffset? FurtherDecisionsPossibleUntil,
    int CalculatorVersion);

/// <param name="RefundRequested">What this decision returns to the customer: requested, and sent by the payment sweep.</param>
public sealed record ResolutionPreviewCustomerDto(MoneyDto RefundRequested);

/// <param name="RetainedShare">The deposit share this decision keeps for Khadra. Never commission.</param>
/// <param name="Commission">Khadra's commission on the booking at finality: the frozen figure, capped at the office's money.</param>
public sealed record ResolutionPreviewPlatformDto(MoneyDto RetainedShare, MoneyDto Commission);

/// <param name="Share">This decision's deposit share to the office, before commission.</param>
/// <param name="Money">All the office's money on the booking before commission: rental revenue and every dispute share.</param>
/// <param name="FrozenCommission">The commission the booking froze when it was made.</param>
/// <param name="Commission">What is taken: the frozen figure, never more than <see cref="Money"/> (owner, 2026-09-29).</param>
/// <param name="Charges">Every charge decided disputes on the booking assessed on the office, this one's included.</param>
/// <param name="Net">The office's net on the booking. Below zero, the office owes.</param>
public sealed record ResolutionPreviewOfficeDto(
    MoneyDto Share,
    MoneyDto Money,
    MoneyDto FrozenCommission,
    MoneyDto Commission,
    MoneyDto Charges,
    MoneyDto Net);

/// <summary>What the booking's EARLIER decided disputes already did, which these figures include.</summary>
public sealed record ResolutionPreviewEarlierDto(
    int Count,
    MoneyDto ToCustomer,
    MoneyDto KeptByPlatform,
    MoneyDto ToOffice,
    MoneyDto ChargedToOffice);
