using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.IdentityAccess;
using Khadra.Application.IdentityAccess.ForgotPassword;
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
    public IAuthEmailComposer EmailComposer { get; } = Substitute.For<IAuthEmailComposer>();
    public IEmailSender EmailSender { get; init; } = Substitute.For<IEmailSender>();
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
    }

    // Registration is shared by the customer and dealer-owner flows; both handlers delegate here.
    public AccountRegistrar Registrar => new(
        UserRepository, VerificationTokens, Hasher, OpaqueTokens, Policy,
        BusinessRules, Calendar, Clock, UnitOfWork, Emails);

    public AuthTokenFactory TokenFactory => new(new StubAccessTokenIssuer(), OpaqueTokens, RefreshTokens, Policy);

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
