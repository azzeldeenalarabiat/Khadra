using Khadra.Domain.Common;

namespace Khadra.Domain.Payables;

public static class PayableErrors
{
    public static readonly Error NotFound =
        Error.NotFound("payables.not_found", "That payable does not exist.");

    public static readonly Error SettlementNotFound =
        Error.NotFound("payables.settlement_not_found", "That settlement does not exist.");

    public static readonly Error OfficeNotFound =
        Error.NotFound("payables.office_not_found", "That rental office does not exist.");

    public static readonly Error NothingDue =
        Error.Conflict("payables.nothing_due", "Nothing is due to or from this office in that currency.");

    public static readonly Error RecordsChanged =
        Error.Conflict(
            "payables.records_changed",
            "A booking in this settlement no longer matches its recorded payable. Nothing was recorded; it will be held for review.");

    public static readonly Error PaidOnInFuture =
        Error.Validation("payables.paid_on_in_future", "The date the money moved cannot be in the future.");

    public static readonly Error SettlementAlreadyVoided =
        Error.Conflict("payables.settlement_already_voided", "That settlement has already been voided.");

    public static readonly Error VoidReasonRequired =
        Error.Validation("payables.void_reason_required", "Say why the settlement is being voided.");

    public static readonly Error VoidReasonTooLong =
        Error.Validation("payables.void_reason_too_long", "The reason is too long.");

    public static readonly Error HoldReasonRequired =
        Error.Validation("payables.hold_reason_required", "Say why the payable is being held.");

    public static readonly Error HoldReasonTooLong =
        Error.Validation("payables.hold_reason_too_long", "The reason is too long.");

    public static readonly Error AlreadyHeld =
        Error.Conflict("payables.already_held", "That payable is already held.");

    /// <summary>A net-zero payable moves no money, so there is nothing to hold back (Wave 4, F56 c).</summary>
    public static readonly Error NothingToHold =
        Error.Conflict("payables.nothing_to_hold", "That payable moves no money either way, so there is nothing to hold back.");

    public static readonly Error NotHeld =
        Error.Conflict("payables.not_held", "That payable is not held by an administrator.");

    public static readonly Error AlreadySettled =
        Error.Conflict("payables.already_settled", "That payable is already settled.");

    public static readonly Error ChangedConcurrently =
        Error.Conflict(
            "payables.changed_concurrently",
            "This office's payables changed while you were recording. Nothing was recorded; review them and try again.");

    /// <summary>
    /// The balance the administrator confirmed is not the balance due now: a booking became due, was held or
    /// released, or another administrator settled first. Carries the balance due now, top-level, so the screen
    /// can show it and ask again — never settle a figure nobody saw.
    /// </summary>
    public static Error BalanceChanged(decimal current, string currency)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);
        return Error.Conflict(
                "payables.balance_changed",
                "The balance due has changed. Review the new amount and confirm again.")
            with
            {
                Extensions = new Dictionary<string, object?>
                {
                    ["currentAmount"] = new Dictionary<string, object?>
                    {
                        ["amount"] = current,
                        ["currency"] = currency,
                    },
                },
            };
    }
}
