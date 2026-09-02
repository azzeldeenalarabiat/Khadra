using CSharpFunctionalExtensions;
using Khadra.Domain.Common;

namespace Khadra.Domain.IdentityAccess;

// Single-use, time-limited token emailed to the user for email verification or password reset.
// Only the SHA-256 hash is stored.
public sealed class VerificationToken : AggregateRoot
{
    public const int TokenHashLength = 64;

    public Id UserId { get; private set; }
    public VerificationPurpose Purpose { get; private set; } = null!;
    public string TokenHash { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? ConsumedAt { get; private set; }

    private VerificationToken()
    {
    }

    private VerificationToken(Id id) : base(id)
    {
    }

    public bool IsConsumed => ConsumedAt is not null;

    public bool IsExpired(DateTimeOffset now) => ExpiresAt <= now;

    public bool IsActive(DateTimeOffset now) => !IsConsumed && !IsExpired(now);

    public static VerificationToken Issue(
        Id userId,
        VerificationPurpose purpose,
        string tokenHash,
        DateTimeOffset now,
        TimeSpan lifetime)
    {
        ArgumentNullException.ThrowIfNull(purpose);
        if (userId.IsEmpty)
            throw new DomainException("A verification token requires a user.");
        if (string.IsNullOrWhiteSpace(tokenHash) || tokenHash.Length != TokenHashLength)
            throw new DomainException("A verification token hash must be a 64-character SHA-256 hex string.");
        if (lifetime <= TimeSpan.Zero)
            throw new DomainException("A verification token lifetime must be positive.");

        return new VerificationToken(Id.New())
        {
            UserId = userId,
            Purpose = purpose,
            TokenHash = tokenHash,
            CreatedAt = now,
            ExpiresAt = now.Add(lifetime)
        };
    }

    public UnitResult<Error> Consume(DateTimeOffset now)
    {
        if (IsConsumed || IsExpired(now))
            return UnitResult.Failure(IdentityErrors.InvalidToken);

        ConsumedAt = now;
        return UnitResult.Success<Error>();
    }

    // Used when a newer token of the same purpose is issued: older links stop working.
    public void Invalidate(DateTimeOffset now) => ConsumedAt ??= now;
}
