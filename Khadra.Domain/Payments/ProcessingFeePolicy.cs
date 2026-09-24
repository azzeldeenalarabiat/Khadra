using Khadra.Domain.Common;

namespace Khadra.Domain.Payments;

/// <summary>What an optional online-payment processing fee is a percentage OF.</summary>
/// <remarks>
/// Deliberately configurable (owner, 2026-09-24): whether a real provider lets the fee apply to the
/// whole full payment, or only to the part the customer chose to pay above the mandatory deposit, is
/// the provider's rule and not yet known.
/// </remarks>
public sealed class ProcessingFeeBasis : Enumeration
{
    public static readonly ProcessingFeeBasis FullAmount = new(1, "FullAmount");
    public static readonly ProcessingFeeBasis AboveDeposit = new(2, "AboveDeposit");

    private ProcessingFeeBasis(int id, string name) : base(id, name)
    {
    }
}

/// <summary>
/// The optional card-processing fee on paying the FULL amount online, as the platform is configured
/// to charge it right now.
/// </summary>
/// <remarks>
/// <para>
/// Never a tax, never commission, never rental: a separate line the customer is shown before paying.
/// Khadra absorbs the processing cost of the mandatory deposit, so the fee never applies to a
/// <see cref="PaymentPurpose.Deposit"/>. Disabled unless configured, and not to be enabled in
/// Production until a real provider confirms it may be charged (owner, 2026-09-24).
/// </para>
/// <para>
/// A payment FREEZES the fee it was opened with (<c>Payment.ProcessingFee</c>), so changing this later
/// never re-prices an open checkout or a past receipt.
/// </para>
/// </remarks>
public sealed record ProcessingFeePolicy(bool Enabled, Percentage Percent, ProcessingFeeBasis Basis, bool Refundable)
{
    public static ProcessingFeePolicy Disabled { get; } =
        new(false, Percentage.FromValidated(0m), ProcessingFeeBasis.FullAmount, true);

    /// <summary>The fee on a payment of this purpose, rounded as every Money is. Zero when off.</summary>
    public Money FeeFor(PaymentPurpose purpose, Money fullPayable, Money deposit)
    {
        ArgumentNullException.ThrowIfNull(purpose);
        ArgumentNullException.ThrowIfNull(fullPayable);
        ArgumentNullException.ThrowIfNull(deposit);

        if (!Enabled || purpose != PaymentPurpose.FullPayment || Percent.Value == 0m)
            return Money.ZeroIn(fullPayable.CurrencyCode);

        var basis = Basis == ProcessingFeeBasis.AboveDeposit
            ? Money.Create(Math.Max(0m, fullPayable.Amount - deposit.Amount), fullPayable.CurrencyCode)
            : fullPayable;
        return Percent.Of(basis);
    }
}
