using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.IdentityAccess;
using Khadra.Application.IdentityAccess.ForgotPassword;
using Khadra.Application.IdentityAccess.Login;
using Khadra.Application.IdentityAccess.ResendVerification;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.IdentityAccess.Repositories;
using Khadra.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Khadra.Tests.Application.IdentityAccess;

// One place to build every collaborator a handler needs; tests override only what they assert on.
internal sealed class AuthHandlerTestContext
{
    public TestClock Clock { get; } = new(Users.Now);
    public FakePasswordHasher Hasher { get; } = new();
    public FakeOpaqueTokens OpaqueTokens { get; } = new();
    public IAuthPolicySettings Policy { get; } = TestAuthPolicy.Default;
    public IUserRepository UserRepository { get; } = Substitute.For<IUserRepository>();
    public IRefreshTokenRepository RefreshTokens { get; } = Substitute.For<IRefreshTokenRepository>();
    public IVerificationTokenRepository VerificationTokens { get; } = Substitute.For<IVerificationTokenRepository>();
    public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();
    public FakeSignInThrottle Throttle { get; } = new();
    public IAuthEmailComposer EmailComposer { get; } = Substitute.For<IAuthEmailComposer>();
    public IEmailSender EmailSender { get; init; } = TestEmail.AcceptingSender();
    public ICurrentActor Actor { get; } = Substitute.For<ICurrentActor>();
    // Recording rather than null, so a test can assert what the reason line says AND that the
    // address never reaches it.
    public RecordingLogger<ForgotPasswordHandler> ForgotPasswordLog { get; } = new();
    public IBusinessRulesProvider BusinessRules { get; } = TestBusinessRules.Provider();
    public IReportingCalendar Calendar { get; } = TestBusinessRules.Calendar();

    public List<RefreshToken> AddedRefreshTokens { get; } = [];
    public List<VerificationToken> AddedVerificationTokens { get; } = [];
    public List<User> AddedUsers { get; } = [];

    public AuthHandlerTestContext()
    {
        UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);
        RefreshTokens
            .When(repository => repository.AddAsync(Arg.Any<RefreshToken>(), Arg.Any<CancellationToken>()))
            .Do(call => AddedRefreshTokens.Add(call.Arg<RefreshToken>()));
        VerificationTokens
            .When(repository => repository.AddAsync(Arg.Any<VerificationToken>(), Arg.Any<CancellationToken>()))
            .Do(call => AddedVerificationTokens.Add(call.Arg<VerificationToken>()));
        UserRepository
            .When(repository => repository.AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>()))
            .Do(call => AddedUsers.Add(call.Arg<User>()));
        EmailComposer.EmailVerification(Arg.Any<User>(), Arg.Any<string>())
            .Returns(call => Message(call.Arg<User>(), "verify", call.Arg<string>()));
        EmailComposer.PasswordReset(Arg.Any<User>(), Arg.Any<string>())
            .Returns(call => Message(call.Arg<User>(), "reset", call.Arg<string>()));
        EmailComposer.PasswordChanged(Arg.Any<User>())
            .Returns(call => Message(call.Arg<User>(), "changed", string.Empty));
        // All five, so no handler here hands a transport a null message.
        EmailComposer.EmployeeInvitation(Arg.Any<User>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(call => Message(call.Arg<User>(), "invited", call.ArgAt<string>(2)));
        EmailComposer.AdminInvitation(Arg.Any<User>(), Arg.Any<string>())
            .Returns(call => Message(call.Arg<User>(), "admin", call.ArgAt<string>(1)));
    }

    /// <summary>The legal texts in force and the consents staged (Wave 4, W4-8): nothing in force unless a test publishes.</summary>
    public TestLegal Legal { get; } = new();

    // Registration is shared by the customer and dealer-owner flows; both handlers delegate here.
    public AccountRegistrar Registrar => new(
        UserRepository, VerificationTokens, Hasher, OpaqueTokens, Policy,
        BusinessRules, Calendar, Clock, UnitOfWork, Emails, Legal.Recorder);

    /// <summary>What the access tokens were minted for, so a test can read the session claim back.</summary>
    public StubAccessTokenIssuer AccessTokens { get; } = new();

    public AuthTokenFactory TokenFactory => new(AccessTokens, OpaqueTokens, RefreshTokens, Policy);

    public AuthEmailDispatcher Emails => new(EmailComposer, EmailSender, NullLogger<AuthEmailDispatcher>.Instance);

    public ResendVerificationHandler ResendVerification() => new(
        UserRepository, VerificationTokens, OpaqueTokens, Policy, Clock, UnitOfWork, Emails);

    public ForgotPasswordHandler ForgotPassword() => new(
        UserRepository, VerificationTokens, OpaqueTokens, Policy, Clock, UnitOfWork, Emails,
        Actor, ForgotPasswordLog);

    public User KnownUser(User user)
    {
        UserRepository.GetByEmailAsync(user.Email, Arg.Any<CancellationToken>()).Returns(user);
        UserRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        return user;
    }

    public IReadOnlyList<EmailMessage> SentEmails() =>
        EmailSender.ReceivedCalls()
            .Select(call => call.GetArguments()[0])
            .OfType<EmailMessage>()
            .ToList();

    private static EmailMessage Message(User user, string kind, string token) =>
        new(user.Email.Value, user.Name.Value, kind, $"{kind}:{token}", $"{kind}:{token}");
}

/// <summary>
/// The per-account sign-in ceiling (pre-launch item 51) in memory, with the shipped policy: eight failures within
/// fifteen minutes refuse the name for fifteen. The SQL behind the real one is proven in <c>SignInThrottleTests</c>.
/// </summary>
internal sealed class FakeSignInThrottle : ISignInThrottle
{
    private readonly Dictionary<string, (DateTimeOffset WindowStart, int Failures, DateTimeOffset? BlockedUntil)> _rows = [];

    public int MaxFailures { get; init; } = 8;
    public TimeSpan Window { get; init; } = TimeSpan.FromMinutes(15);
    public TimeSpan Block { get; init; } = TimeSpan.FromMinutes(15);

    public List<string> Resets { get; } = [];

    public int FailuresOf(string email) => _rows.TryGetValue(email, out var row) ? row.Failures : 0;

    public Task<TimeSpan?> BlockedForAsync(EmailAddress subject, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        Task.FromResult<TimeSpan?>(
            _rows.TryGetValue(subject.Value, out var row) && row.BlockedUntil is { } until && until > now ? until - now : null);

    public Task RecordFailureAsync(EmailAddress subject, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var row = _rows.TryGetValue(subject.Value, out var found) && found.WindowStart > now - Window
            ? (found.WindowStart, found.Failures + 1, found.BlockedUntil)
            : (now, 1, found.BlockedUntil);
        if (row.Item2 >= MaxFailures)
            row.Item3 = now + Block;
        _rows[subject.Value] = row;
        return Task.CompletedTask;
    }

    public Task ResetAsync(EmailAddress subject, CancellationToken cancellationToken = default)
    {
        _rows.Remove(subject.Value);
        Resets.Add(subject.Value);
        return Task.CompletedTask;
    }
}
