using Khadra.Domain.IdentityAccess;

namespace Khadra.Application.IdentityAccess.Login;

/// <summary>
/// Failed sign-ins counted per ACCOUNT NAME, across every address (pre-launch item 51).
/// </summary>
/// <remarks>
/// <para>
/// The rate limiter in front of the sign-in route is keyed on the pair of address and account name, so a guesser spread
/// across many addresses met no ceiling on one account at all, and it counts every request rather than every failure.
/// This counts FAILURES of one account name, wherever they come from, and refuses further attempts for a while once
/// there are too many. The figures are configuration (<c>Authentication:SignInThrottle</c>); the limiter stays as it was.
/// </para>
/// <para>
/// It is keyed on the normalised address that was TYPED, whether or not an account holds it, and answers the same
/// either way: counting only real accounts would make the refusal itself say which addresses are registered.
/// </para>
/// </remarks>
public interface ISignInThrottle
{
    /// <summary>How much longer this name is refused, or null when it is not.</summary>
    Task<TimeSpan?> BlockedForAsync(EmailAddress subject, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>Counts one failed attempt, and starts the refusal when it is one too many. Committed at once.</summary>
    Task RecordFailureAsync(EmailAddress subject, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>Forgets every failure of this name: it signed in, or its owner proved the mailbox.</summary>
    Task ResetAsync(EmailAddress subject, CancellationToken cancellationToken = default);
}
