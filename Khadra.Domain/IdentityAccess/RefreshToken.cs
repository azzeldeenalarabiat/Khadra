using CSharpFunctionalExtensions;
using Khadra.Domain.Common;

namespace Khadra.Domain.IdentityAccess;

// One row per issued refresh token. Tokens are single-use and grouped into a family that starts at
// login; presenting an already-rotated token is replay and revokes the whole family.
// Only the SHA-256 hash is stored, never the raw token.
public sealed class RefreshToken : AggregateRoot
{
    public const int TokenHashLength = 64;

    public Id UserId { get; private set; }
    public Guid FamilyId { get; private set; }
    public string TokenHash { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    // Absolute deadline inherited by every replacement in the family: rotation never extends a session forever.
    public DateTimeOffset FamilyExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public Id? ReplacedByTokenId { get; private set; }
    public string? CreatedByIp { get; private set; }
    public string? UserAgent { get; private set; }

    private RefreshToken()
    {
    }

    private RefreshToken(Id id) : base(id)
    {
    }

    public bool IsRevoked => RevokedAt is not null;

    public bool IsExpired(DateTimeOffset now) => ExpiresAt <= now || FamilyExpiresAt <= now;

    public bool IsActive(DateTimeOffset now) => !IsRevoked && !IsExpired(now);

    public static RefreshToken IssueNewFamily(
        Id userId,
        string tokenHash,
        DateTimeOffset now,
        TimeSpan lifetime,
        TimeSpan familyLifetime,
        string? createdByIp,
        string? userAgent)
    {
        if (familyLifetime < lifetime)
            throw new DomainException("The family lifetime cannot be shorter than a single token lifetime.");

        var familyExpiresAt = now.Add(familyLifetime);
        return Create(userId, Guid.NewGuid(), tokenHash, now, lifetime, familyExpiresAt, createdByIp, userAgent);
    }

    // The replacement stays in this token's family and can never outlive the family deadline.
    public RefreshToken IssueReplacement(
        string tokenHash,
        DateTimeOffset now,
        TimeSpan lifetime,
        string? createdByIp,
        string? userAgent) =>
        Create(UserId, FamilyId, tokenHash, now, lifetime, FamilyExpiresAt, createdByIp, userAgent);

    private static RefreshToken Create(
        Id userId,
        Guid familyId,
        string tokenHash,
        DateTimeOffset now,
        TimeSpan lifetime,
        DateTimeOffset familyExpiresAt,
        string? createdByIp,
        string? userAgent)
    {
        if (userId.IsEmpty)
            throw new DomainException("A refresh token requires a user.");
        if (string.IsNullOrWhiteSpace(tokenHash) || tokenHash.Length != TokenHashLength)
            throw new DomainException("A refresh token hash must be a 64-character SHA-256 hex string.");
        if (lifetime <= TimeSpan.Zero)
            throw new DomainException("A refresh token lifetime must be positive.");

        var expiresAt = now.Add(lifetime);
        if (expiresAt > familyExpiresAt)
            expiresAt = familyExpiresAt;

        return new RefreshToken(Id.New())
        {
            UserId = userId,
            FamilyId = familyId,
            TokenHash = tokenHash,
            CreatedAt = now,
            ExpiresAt = expiresAt,
            FamilyExpiresAt = familyExpiresAt,
            CreatedByIp = Truncate(createdByIp, 45),
            UserAgent = Truncate(userAgent, 256)
        };
    }

    // Marks this token as consumed by `replacementId`. Fails (without mutating) on replay or expiry.
    public UnitResult<Error> Rotate(DateTimeOffset now, Id replacementId)
    {
        if (IsRevoked || IsExpired(now))
            return UnitResult.Failure(IdentityErrors.InvalidRefreshToken);
        if (replacementId.IsEmpty)
            throw new DomainException("A replacement token id is required.");

        RevokedAt = now;
        ReplacedByTokenId = replacementId;
        return UnitResult.Success<Error>();
    }

    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
