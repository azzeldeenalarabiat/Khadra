namespace Khadra.Domain.Payments;

/// <summary>
/// The provider names this platform writes onto a payment row, and what they mean afterwards.
/// </summary>
/// <remarks>
/// <para>
/// <c>Payment.Provider</c> is not a configuration echo. It is a permanent property of the record: it
/// is written when the attempt is opened, never changes, and is what tells somebody reading the
/// database in two years which rentals had money behind them. That makes the meaning of one of those
/// strings a DOMAIN fact rather than an infrastructure detail, which is why the constant lives here
/// and everything else — the options class, the adapter, the two startup guards, the DTO — aliases
/// it. One symbol, so the guard and the row can never disagree about what "sandbox" spells.
/// </para>
/// <para>
/// Real provider names are not listed. They are whatever the adapter reports, and the platform has no
/// opinion about them beyond "not the sandbox".
/// </para>
/// </remarks>
public static class PaymentProviders
{
    /// <summary>
    /// The provider that completes a checkout and moves no money. NEVER Production.
    /// </summary>
    /// <remarks>
    /// Deliberately shouted, so that it is unmistakable in a database dump, a log line and a support
    /// conversation. Anything vaguer — "Test", "Dev" — reads like a provider's own name once it is a
    /// column value, which is the confusion the whole marker exists to prevent.
    /// </remarks>
    public const string Sandbox = "SANDBOX";

    /// <summary>The absence of a provider. No row is ever written with it.</summary>
    /// <remarks>
    /// It is a configuration value and a reported name, not a stored one: a platform with no provider
    /// opens no attempts, so nothing can carry it. It lives beside <see cref="Sandbox"/> because the
    /// two are the closed set a deployment chooses between today.
    /// </remarks>
    public const string None = "None";

    /// <summary>Whether a stored provider name is the sandbox's.</summary>
    /// <remarks>
    /// Ordinal, because the value compared against is always the constant a provider's own
    /// <c>Name</c> returned rather than something a person typed. Configuration is matched
    /// case-insensitively at startup; a ROW is not, and a row that did not come from the constant is
    /// a real provider whose name happens to look like it.
    /// </remarks>
    public static bool IsSandbox(string? provider) =>
        string.Equals(provider, Sandbox, StringComparison.Ordinal);
}
