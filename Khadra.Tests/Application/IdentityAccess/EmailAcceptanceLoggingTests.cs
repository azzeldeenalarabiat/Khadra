using Khadra.Application.Common.Ports;
using Khadra.Application.IdentityAccess;
using Khadra.Domain.IdentityAccess;
using Khadra.Tests.Support;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Khadra.Tests.Application.IdentityAccess;

/// <summary>
/// The line that answers "did the verification email go?": what it proves, and what it must never say.
/// </summary>
/// <remarks>
/// A send that worked used to leave no trace: the log recorded failures only, so "the customer says
/// nothing arrived" had nowhere to start. The line is written now, from the transport's receipt, and it
/// says ACCEPTED because that is all a transport can vouch for. The provider's message id is what joins
/// it to the provider's own log, which is where delivery is recorded.
/// </remarks>
public sealed class EmailAcceptanceLoggingTests
{
    private const int Failed = 1100;
    private const int Accepted = 1101;
    private const string Token = "tok-4b7d19e2c6a80f35";
    private const string BrevoMessageId = "<202609170930.4242@smtp-relay.mailin.fr>";

    private static (AuthEmailDispatcher Dispatcher, RecordingLogger<AuthEmailDispatcher> Log) Build(IEmailSender sender)
    {
        var composer = Substitute.For<IAuthEmailComposer>();
        composer.EmailVerification(Arg.Any<User>(), Arg.Any<string>())
            .Returns(call => new EmailMessage(
                call.Arg<User>().Email.Value,
                call.Arg<User>().Name.Value,
                "Verify your Khadra email address",
                $"<a href=\"https://app.khadra.test/verify-email?token={call.Arg<string>()}\">Verify</a>",
                $"Verify: https://app.khadra.test/verify-email?token={call.Arg<string>()}"));
        var log = new RecordingLogger<AuthEmailDispatcher>();
        return (new AuthEmailDispatcher(composer, sender, log), log);
    }

    private static IEmailSender BrevoAccepting()
    {
        var sender = Substitute.For<IEmailSender>();
        sender.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(new EmailSendReceipt("Brevo", BrevoMessageId, TestEmail.AcceptedAt, 1, null));
        return sender;
    }

    [Fact]
    public async Task An_accepted_email_is_logged_with_the_providers_receipt_and_says_accepted_not_delivered()
    {
        var user = Users.Customer(verified: false);
        var (dispatcher, log) = Build(BrevoAccepting());

        var sent = await dispatcher.SendEmailVerificationAsync(user, Token, CancellationToken.None);

        Assert.True(sent);
        var line = Assert.Single(log.Entries, entry => entry.Id.Id == Accepted);
        Assert.Equal(LogLevel.Information, line.Level);
        Assert.Contains("accepted by Brevo", line.Message, StringComparison.Ordinal);
        Assert.Contains(BrevoMessageId, line.Message, StringComparison.Ordinal);
        Assert.Contains(user.Id.ToString(), line.Message, StringComparison.Ordinal);
        Assert.Contains("Accepted is not delivered", line.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_line_names_the_recipients_domain_and_nothing_else_about_them_or_the_message()
    {
        var user = Users.Customer(verified: false);
        var address = user.Email.Value;
        var (dispatcher, log) = Build(BrevoAccepting());

        await dispatcher.SendEmailVerificationAsync(user, Token, CancellationToken.None);

        Assert.Contains(address[(address.LastIndexOf('@') + 1)..], log.AllText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(address, log.AllText, StringComparison.OrdinalIgnoreCase);
        // The token is the credential the link carries, and the link is how it would get there.
        Assert.DoesNotContain(Token, log.AllText, StringComparison.Ordinal);
        Assert.DoesNotContain("verify-email", log.AllText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_refused_email_is_logged_as_a_failure_and_never_as_accepted()
    {
        var user = Users.Customer(verified: false);
        var sender = Substitute.For<IEmailSender>();
        sender.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns<Task<EmailSendReceipt>>(_ =>
                throw new InvalidOperationException("Brevo refused the message (401 Unauthorized)"));
        var (dispatcher, log) = Build(sender);

        var sent = await dispatcher.SendEmailVerificationAsync(user, Token, CancellationToken.None);

        Assert.False(sent);
        Assert.True(log.Logged(Failed));
        Assert.False(log.Logged(Accepted));
        Assert.DoesNotContain(Token, log.AllText, StringComparison.Ordinal);
    }
}
