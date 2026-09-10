using Khadra.Application.IdentityAccess.ForgotPassword;
using Khadra.Domain.IdentityAccess;
using Khadra.Infrastructure.Notifications;
using Khadra.Tests.Support;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Khadra.Tests.Application.IdentityAccess;

/// <summary>
/// Why a reset produced no email, said where an operator can read it and a caller cannot.
/// </summary>
/// <remarks>
/// Written after production answered <c>202 Accepted</c> to every password reset while sending
/// nothing at all. The public answer is the same whatever happens — it has to be, or the form becomes
/// a way to ask who holds an account — so the only place the reason can go is the log, and until now
/// nothing put it there. The handler returned success from three different situations and left no
/// trace of which.
///
/// These tests pin both halves: that the reason IS logged, and that the address is NOT. A diagnostic
/// that names the account it declined to find would undo the very protection the silent response
/// exists to provide.
/// </remarks>
public sealed class ForgotPasswordDiagnosticsTests
{
    private const int AddressUnusable = 1300;
    private const int NoAccount = 1301;
    private const int AccountDeleted = 1302;
    private const int ResetSent = 1303;
    private const int ResetNotSent = 1304;

    private static AuthHandlerTestContext Context()
    {
        var context = new AuthHandlerTestContext();
        context.Actor.CorrelationId.Returns("corr-42");
        return context;
    }

    /// <summary>Nothing was looked up, because the value submitted was not an address.</summary>
    [Fact]
    public async Task An_unusable_address_says_so_and_still_answers_the_caller_the_same_way()
    {
        var context = Context();

        var result = await context.ForgotPassword()
            .Handle(new ForgotPasswordCommand("not-an-address"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(context.SentEmails());
        Assert.True(context.ForgotPasswordLog.Logged(AddressUnusable));
        Assert.Contains("corr-42", context.ForgotPasswordLog.AllText, StringComparison.Ordinal);
    }

    /// <summary>The ordinary case: a typo. Reported, because it is indistinguishable from a fault.</summary>
    [Fact]
    public async Task No_matching_account_says_so_without_naming_the_address()
    {
        var context = Context();
        context.UserRepository.GetByEmailAsync(Arg.Any<EmailAddress>(), Arg.Any<CancellationToken>())
            .Returns((User?)null);
        context.UserRepository.ExistsByEmailAsync(Arg.Any<EmailAddress>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await context.ForgotPassword()
            .Handle(new ForgotPasswordCommand("ghost@example.jo"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(context.SentEmails());
        Assert.True(context.ForgotPasswordLog.Logged(NoAccount));
        Assert.DoesNotContain("ghost@example.jo", context.ForgotPasswordLog.AllText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ghost", context.ForgotPasswordLog.AllText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The one that looks identical from outside and is not. <c>GetByEmailAsync</c> honours the
    /// soft-delete filter while <c>ExistsByEmailAsync</c> ignores it, so a row that is present and
    /// excluded is reported as such — at Warning, because "the account is there but deleted" is the
    /// answer nobody guesses and everybody needs.
    /// </summary>
    [Fact]
    public async Task A_soft_deleted_account_is_reported_as_deleted_rather_than_missing()
    {
        var context = Context();
        context.UserRepository.GetByEmailAsync(Arg.Any<EmailAddress>(), Arg.Any<CancellationToken>())
            .Returns((User?)null);
        context.UserRepository.ExistsByEmailAsync(Arg.Any<EmailAddress>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await context.ForgotPassword()
            .Handle(new ForgotPasswordCommand("rana@example.jo"), CancellationToken.None);

        // Still success, still nothing sent: a deleted account must not be resettable, and must not
        // announce itself to the caller either.
        Assert.True(result.IsSuccess);
        Assert.Empty(context.SentEmails());
        Assert.True(context.ForgotPasswordLog.Logged(AccountDeleted));
        Assert.False(context.ForgotPasswordLog.Logged(NoAccount));
        Assert.Contains(
            context.ForgotPasswordLog.Entries,
            entry => entry.Id.Id == AccountDeleted && entry.Level == LogLevel.Warning);
        Assert.DoesNotContain("rana@example.jo", context.ForgotPasswordLog.AllText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A sent link is logged too, because it is the ABSENCE of this line beside "Handled
    /// ForgotPasswordCommand" that says the handler returned early — and that line is written from a
    /// finally block, so it appears even when nothing was done.
    /// </summary>
    [Fact]
    public async Task A_sent_link_is_logged_without_the_address_or_the_token()
    {
        var context = Context();
        var user = context.KnownUser(Users.Customer());

        var result = await context.ForgotPassword()
            .Handle(new ForgotPasswordCommand(user.Email.Value), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(context.ForgotPasswordLog.Logged(ResetSent));

        var logged = context.ForgotPasswordLog.AllText;
        Assert.DoesNotContain(user.Email.Value, logged, StringComparison.OrdinalIgnoreCase);
        // The raw token is the credential the link carries. It must never be recoverable from a log.
        Assert.DoesNotContain(context.OpaqueTokens.Issued.Single().Value, logged, StringComparison.Ordinal);
        // The user id is deliberately present: opaque, already in every audit row, and the only way
        // to follow one person's resets across a log.
        Assert.Contains(user.Id.ToString(), logged, StringComparison.Ordinal);
    }

    /// <summary>A refused send is reported to the caller AND named in the log.</summary>
    [Fact]
    public async Task A_refused_send_is_logged_as_refused()
    {
        var context = new AuthHandlerTestContext
        {
            EmailSender = Substitute.For<Khadra.Application.Common.Ports.IEmailSender>(),
        };
        context.Actor.CorrelationId.Returns("corr-99");
        context.EmailSender
            .SendAsync(Arg.Any<Khadra.Application.Common.Ports.EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(new InvalidOperationException("the relay refused it")));
        var user = context.KnownUser(Users.Customer());

        var result = await context.ForgotPassword()
            .Handle(new ForgotPasswordCommand(user.Email.Value), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.True(context.ForgotPasswordLog.Logged(ResetNotSent));
        Assert.DoesNotContain(user.Email.Value, context.ForgotPasswordLog.AllText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The first guess anyone makes when no mail arrives, answered once and for all: neither
    /// suspension nor an unverified address stops a reset.
    ///
    /// Both are deliberate. A suspended person still owns their address, and somebody who never
    /// confirmed theirs is exactly the person likely to have forgotten the password — refusing them
    /// would lock out the account permanently. Pinned so it is a decision rather than an accident.
    /// </summary>
    [Fact]
    public async Task A_suspended_account_still_gets_a_reset_link()
    {
        var context = Context();
        var user = Users.Customer();
        Assert.True(user.Suspend("under review", Users.Now).IsSuccess);
        context.KnownUser(user);

        var result = await context.ForgotPassword()
            .Handle(new ForgotPasswordCommand(user.Email.Value), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(context.SentEmails());
        Assert.True(context.ForgotPasswordLog.Logged(ResetSent));
    }

    [Fact]
    public async Task An_unverified_account_still_gets_a_reset_link()
    {
        var context = Context();
        var user = context.KnownUser(Users.Customer(verified: false));

        var result = await context.ForgotPassword()
            .Handle(new ForgotPasswordCommand(user.Email.Value), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(context.SentEmails());
        Assert.False(user.IsEmailVerified);
    }
}

/// <summary>
/// What the startup check says when the configured transport delivers nothing.
/// </summary>
/// <remarks>
/// This probe used to report READY, so a boot log read:
/// <c>"Email ready. Email:Provider is 'Logging'. Messages are written to the log and delivered to
/// nobody."</c> — a sentence at war with itself, whose first two words are the only part anyone
/// scanning a log reads. Nothing else in the system said mail was not going out, and a production
/// platform ran on it.
/// </remarks>
public sealed class LoggingTransportProbeTests
{
    [Fact]
    public async Task The_logging_transport_is_never_reported_as_ready()
    {
        var status = await new LoggingTransportProbe("Logging").CheckAsync(CancellationToken.None);

        Assert.False(status.IsReady);
        Assert.Contains("NOTHING IS DELIVERED", status.Description, StringComparison.Ordinal);
    }

    /// <summary>
    /// The transport is chosen by string match with a silent fallback, so a typo selects the one that
    /// delivers nothing. Naming the value that was read turns "why is it Logging?" into "because this
    /// is what Email__Provider says".
    /// </summary>
    [Fact]
    public async Task A_provider_that_matches_nothing_is_named_in_the_description()
    {
        var status = await new LoggingTransportProbe("Brevoo").CheckAsync(CancellationToken.None);

        Assert.False(status.IsReady);
        Assert.Contains("Brevoo", status.Description, StringComparison.Ordinal);
        Assert.Contains("matches no known transport", status.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unset_provider_says_it_is_unset_rather_than_naming_a_value()
    {
        var status = await new LoggingTransportProbe(null).CheckAsync(CancellationToken.None);

        Assert.False(status.IsReady);
        Assert.Contains("is not set", status.Description, StringComparison.Ordinal);
    }
}
