using System.Security.Cryptography;
using System.Text;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;

namespace Khadra.Tests.Support;

internal sealed class TestClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = now;

    public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
}

// Deterministic, reversible stand-in for BCrypt so handler tests stay fast and readable.
internal sealed class FakePasswordHasher : IPasswordHasher
{
    public int VerifyCalls { get; private set; }

    public string DummyHash => "hashed:dummy";

    public string Hash(string password) => "hashed:" + password;

    public bool Verify(string password, string hash)
    {
        VerifyCalls++;
        return hash == "hashed:" + password;
    }
}

internal sealed class FakeOpaqueTokens : IOpaqueTokenService
{
    public List<GeneratedOpaqueToken> Issued { get; } = [];

    public GeneratedOpaqueToken Generate()
    {
        var value = Guid.NewGuid().ToString("N");
        var token = new GeneratedOpaqueToken(value, Hash(value));
        Issued.Add(token);
        return token;
    }

    public string Hash(string rawToken) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
}

internal sealed record TestAuthPolicy(
    TimeSpan RefreshTokenLifetime,
    TimeSpan RefreshFamilyLifetime,
    TimeSpan RefreshReuseGrace,
    TimeSpan EmailVerificationLifetime,
    TimeSpan PasswordResetLifetime,
    TimeSpan EmployeeInvitationLifetime,
    int PasswordMinimumLength) : IAuthPolicySettings
{
    public static TestAuthPolicy Default { get; } = new(
        TimeSpan.FromDays(14),
        TimeSpan.FromDays(30),
        // The configured production value: tests exercise the rule the platform runs under.
        TimeSpan.FromSeconds(60),
        TimeSpan.FromHours(24),
        TimeSpan.FromMinutes(60),
        TimeSpan.FromDays(7),
        8);
}

internal sealed class StubAccessTokenIssuer : IAccessTokenIssuer
{
    public IssuedAccessToken Issue(User user, DateTimeOffset now) =>
        new($"access-for-{user.Id}", now.AddMinutes(15));
}

internal static class Users
{
    public static readonly DateTimeOffset Now = new(2026, 9, 3, 10, 0, 0, TimeSpan.Zero);

    // Comfortably over any minimum the platform might configure (the domain caps it at 30).
    public static readonly DateOnly AdultBirthDate = new(1990, 1, 1);

    public static User Customer(
        string email = "ali@example.com",
        string phone = "0791234567",
        string password = "Passw0rd1",
        bool verified = true,
        DateTimeOffset? now = null)
    {
        var user = User.RegisterCustomer(
            EmailAddress.Create(email).Value,
            PhoneNumber.Create(phone).Value,
            PersonName.Create("Ali Ahmad").Value,
            PasswordHash.FromHash("hashed:" + password),
            now ?? Now);
        if (verified)
            user.VerifyEmail(now ?? Now);
        user.ClearDomainEvents();
        return user;
    }

    public static RefreshToken ActiveRefreshToken(User user, FakeOpaqueTokens tokens, DateTimeOffset now, out string rawValue)
    {
        var generated = tokens.Generate();
        rawValue = generated.Value;
        return RefreshToken.IssueNewFamily(user.Id, generated.Hash, now, TimeSpan.FromDays(14), TimeSpan.FromDays(30), "127.0.0.1", "xunit");
    }

    public static Id NewId() => Id.New();
}
