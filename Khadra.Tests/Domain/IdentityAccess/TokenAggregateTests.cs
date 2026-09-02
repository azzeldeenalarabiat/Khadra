using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Tests.Support;

namespace Khadra.Tests.Domain.IdentityAccess;

public sealed class RefreshTokenTests
{
    private static readonly DateTimeOffset Now = Users.Now;
    private static readonly string Hash = new('a', 64);
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(14);
    private static readonly TimeSpan FamilyLifetime = TimeSpan.FromDays(30);

    private static RefreshToken NewFamily() =>
        RefreshToken.IssueNewFamily(Id.New(), Hash, Now, Lifetime, FamilyLifetime, "10.0.0.1", "xunit");

    [Fact]
    public void A_new_family_expires_by_token_and_by_family_deadline()
    {
        var token = NewFamily();

        Assert.NotEqual(Guid.Empty, token.FamilyId);
        Assert.Equal(Now.Add(Lifetime), token.ExpiresAt);
        Assert.Equal(Now.Add(FamilyLifetime), token.FamilyExpiresAt);
        Assert.True(token.IsActive(Now));
        Assert.False(token.IsActive(Now.Add(Lifetime)));
    }

    [Fact]
    public void Rotation_consumes_the_token_and_links_the_replacement_in_the_same_family()
    {
        var token = NewFamily();
        var replacement = token.IssueReplacement(new string('b', 64), Now.AddHours(1), Lifetime, null, null);

        var rotated = token.Rotate(Now.AddHours(1), replacement.Id);

        Assert.True(rotated.IsSuccess);
        Assert.True(token.IsRevoked);
        Assert.Equal(replacement.Id, token.ReplacedByTokenId);
        Assert.Equal(token.FamilyId, replacement.FamilyId);
        Assert.Equal(token.FamilyExpiresAt, replacement.FamilyExpiresAt);
        Assert.True(replacement.IsActive(Now.AddHours(1)));
    }

    [Fact]
    public void Replacements_never_outlive_the_family_deadline()
    {
        var token = NewFamily();
        var lateNow = Now.Add(FamilyLifetime).AddDays(-1);

        var replacement = token.IssueReplacement(new string('c', 64), lateNow, Lifetime, null, null);

        Assert.Equal(token.FamilyExpiresAt, replacement.ExpiresAt);
    }

    [Fact]
    public void Rotating_twice_is_replay_and_fails_without_mutating()
    {
        var token = NewFamily();
        token.Rotate(Now, Id.New());
        var firstReplacement = token.ReplacedByTokenId;

        var replay = token.Rotate(Now.AddMinutes(1), Id.New());

        Assert.True(replay.IsFailure);
        Assert.Equal("auth.invalid_refresh_token", replay.Error.Code);
        Assert.Equal(firstReplacement, token.ReplacedByTokenId);
    }

    [Fact]
    public void Expired_tokens_cannot_rotate()
    {
        var token = NewFamily();

        var result = token.Rotate(Now.Add(Lifetime), Id.New());

        Assert.True(result.IsFailure);
        Assert.False(token.IsRevoked);
    }

    [Fact]
    public void Revoke_is_idempotent_and_keeps_the_first_timestamp()
    {
        var token = NewFamily();

        token.Revoke(Now);
        token.Revoke(Now.AddHours(1));

        Assert.Equal(Now, token.RevokedAt);
    }

    [Fact]
    public void Guards_programming_errors()
    {
        Assert.Throws<DomainException>(() => RefreshToken.IssueNewFamily(Id.New(), Hash, Now, FamilyLifetime, Lifetime, null, null));
        Assert.Throws<DomainException>(() => RefreshToken.IssueNewFamily(Id.New(), "short", Now, Lifetime, FamilyLifetime, null, null));
        Assert.Throws<DomainException>(() => RefreshToken.IssueNewFamily(Id.Empty, Hash, Now, Lifetime, FamilyLifetime, null, null));
        Assert.Throws<DomainException>(() => NewFamily().Rotate(Now, Id.Empty));
    }

    [Fact]
    public void Client_metadata_is_trimmed_and_truncated()
    {
        var token = RefreshToken.IssueNewFamily(Id.New(), Hash, Now, Lifetime, FamilyLifetime, "  ", new string('u', 300));

        Assert.Null(token.CreatedByIp);
        Assert.Equal(256, token.UserAgent!.Length);
    }
}

public sealed class VerificationTokenTests
{
    private static readonly DateTimeOffset Now = Users.Now;
    private static readonly string Hash = new('d', 64);

    private static VerificationToken Issue() =>
        VerificationToken.Issue(Id.New(), VerificationPurpose.EmailVerification, Hash, Now, TimeSpan.FromHours(24));

    [Fact]
    public void Consumes_once_within_its_lifetime()
    {
        var token = Issue();

        Assert.True(token.IsActive(Now));
        Assert.True(token.Consume(Now.AddHours(1)).IsSuccess);
        Assert.Equal(Now.AddHours(1), token.ConsumedAt);
        Assert.False(token.IsActive(Now.AddHours(2)));
    }

    [Fact]
    public void Cannot_be_consumed_twice_or_after_expiry()
    {
        var consumed = Issue();
        consumed.Consume(Now);
        var expired = Issue();

        Assert.Equal("auth.invalid_token", consumed.Consume(Now).Error.Code);
        Assert.Equal("auth.invalid_token", expired.Consume(Now.AddHours(24)).Error.Code);
    }

    [Fact]
    public void Invalidate_is_idempotent()
    {
        var token = Issue();

        token.Invalidate(Now);
        token.Invalidate(Now.AddMinutes(1));

        Assert.Equal(Now, token.ConsumedAt);
        Assert.True(token.Consume(Now).IsFailure);
    }

    [Fact]
    public void Guards_programming_errors()
    {
        Assert.Throws<DomainException>(() => VerificationToken.Issue(Id.Empty, VerificationPurpose.PasswordReset, Hash, Now, TimeSpan.FromHours(1)));
        Assert.Throws<DomainException>(() => VerificationToken.Issue(Id.New(), VerificationPurpose.PasswordReset, "nope", Now, TimeSpan.FromHours(1)));
        Assert.Throws<DomainException>(() => VerificationToken.Issue(Id.New(), VerificationPurpose.PasswordReset, Hash, Now, TimeSpan.Zero));
    }
}
