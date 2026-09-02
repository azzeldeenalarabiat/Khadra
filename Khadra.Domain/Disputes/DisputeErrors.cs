using Khadra.Domain.Common;

namespace Khadra.Domain.Disputes;

public static class DisputeErrors
{
    public static readonly Error ReasonRequired =
        Error.Validation("dispute.reason_required", "A reason is required to open a ticket.");

    public static readonly Error AlreadyResolved =
        Error.Conflict("dispute.already_resolved", "This ticket has already been resolved.");

    public static readonly Error AlreadyWithdrawn =
        Error.Conflict("dispute.already_withdrawn", "This ticket has been withdrawn.");

    public static readonly Error NotOpen =
        Error.Conflict("dispute.not_open", "This ticket is no longer open.");

    public static readonly Error OnlyOpenerCanWithdraw =
        Error.Forbidden("dispute.only_opener_can_withdraw", "Only the party who opened the ticket can withdraw it.");

    public static readonly Error ResolutionNoteRequired =
        Error.Validation("dispute.resolution_note_required", "A resolution note is required so both parties can see the reasoning.");

    public static readonly Error DispositionDoesNotBalance =
        Error.Validation("dispute.disposition_unbalanced", "The refund, platform and dealer amounts must add up to exactly the deposit held.");

    public static readonly Error DispositionCurrencyMismatch =
        Error.Validation("dispute.disposition_currency_mismatch", "All amounts in a resolution must use the same currency.");

    public static readonly Error StatementRequired =
        Error.Validation("dispute.statement_required", "A statement cannot be empty.");
}
