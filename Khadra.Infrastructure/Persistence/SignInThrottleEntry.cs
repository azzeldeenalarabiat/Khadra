namespace Khadra.Infrastructure.Persistence;

/// <summary>
/// Failed sign-ins of one account name (pre-launch item 51). Not a domain aggregate: written only by the atomic
/// upsert in <c>SignInThrottle</c>, deleted on a successful sign-in, and swept once its window and any block are over.
/// </summary>
/// <remarks>
/// Keyed on a hash of the normalised address that was typed — never the address itself, and whether or not an account
/// holds it — so the table neither keeps a list of what strangers typed nor tells registered names from the rest.
/// </remarks>
internal sealed class SignInThrottleEntry
{
    public const int SubjectHashLength = 64;

    public string SubjectHash { get; set; } = null!;
    public DateTimeOffset WindowStartedAt { get; set; }
    public int Failures { get; set; }
    public DateTimeOffset? BlockedUntil { get; set; }
}
