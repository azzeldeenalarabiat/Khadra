namespace Khadra.Infrastructure.Payments;

/// <summary>
/// Whether this process has proved that its database's payments are the same kind of money as its provider
/// (pre-launch item 221). Until it has, <see cref="VerifiedPaymentProvider"/> holds every payment operation.
/// </summary>
/// <remarks>
/// <para>
/// The latch starts CLOSED and moves once: to <see cref="IsVerified"/> on a clean read of the guard's own query
/// (<see cref="PaymentDatabaseCheck"/>), or to <see cref="IsRefused"/> when that read finds the other kind of money.
/// Nothing else opens it — not a successful request, not any other read: a <c>users</c> query that answers proves
/// nothing about <c>payments</c>.
/// </para>
/// <para>
/// A database that answers at boot settles it there, exactly as before: clean opens it, mismatched refuses to start.
/// One that does not answer leaves it closed, and <c>DeferredStartupService</c> asks again until the database does.
/// So a temporary outage no longer stops the API, and a mismatched database still never sees a payment operation —
/// it is refused the moment it can be read.
/// </para>
/// </remarks>
public sealed class PaymentVerification
{
    private const int Unverified = 0;
    private const int Verified = 1;
    private const int Refused = 2;

    private int _state = Unverified;

    public bool IsVerified => Volatile.Read(ref _state) == Verified;

    public bool IsRefused => Volatile.Read(ref _state) == Refused;

    /// <summary>Verified or refused: there is nothing left to ask.</summary>
    public bool IsSettled => Volatile.Read(ref _state) != Unverified;

    /// <summary>A clean read of the guard's query. Ignored once refused: a refusal is final.</summary>
    public void MarkVerified() => Interlocked.CompareExchange(ref _state, Verified, Unverified);

    /// <summary>The guard found the other kind of money. Final.</summary>
    public void MarkRefused() => Interlocked.Exchange(ref _state, Refused);
}
