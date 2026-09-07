using Khadra.Application.Common.Ports;
using Khadra.Application.IdentityAccess;
using Khadra.Application.IdentityAccess.RegisterDealerOwner;
using Khadra.Application.IdentityAccess.ForgotPassword;
using Khadra.Application.IdentityAccess.ResendVerification;
using Khadra.Domain.IdentityAccess;
using Khadra.Tests.Support;
using NSubstitute;

namespace Khadra.Tests.Application.IdentityAccess;

/// <summary>
/// What the console is told when the verification email does not go out.
///
/// `AuthEmailDispatcher` used to return void: it caught every delivery failure, logged it, and let
/// the caller carry on as if the message had gone. The registration screen then said "we sent a
/// verification link to you" whatever had actually happened, and the one fact the person needed —
/// that nothing was coming and they should ask for another link — existed only in a server log they
/// cannot read. A false confirmation is worse than an error: it sends someone to wait at an empty
/// inbox with no reason to suspect anything is wrong.
///
/// The account is still created either way. It is committed before the send is attempted, and a mail
/// outage is no reason to throw away a valid registration.
/// </summary>
public sealed class EmailDeliveryReportingTests
{
    private static RegisterDealerOwnerCommand Command() =>
        new("owner@gallery.jo", "Passw0rd1", "Rami Odeh", "0795554444");

    /// <summary>A sender that refuses everything, the way a bad password or a blocked port would.</summary>
    private static IEmailSender RefusingSender()
    {
        var sender = Substitute.For<IEmailSender>();
        sender.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("The mail server refused the message."));
        return sender;
    }

    [Fact]
    public async Task A_registration_whose_email_was_sent_says_so()
    {
        var context = new AuthHandlerTestContext();

        var result = await new RegisterDealerOwnerHandler(context.Registrar)
            .Handle(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.VerificationEmailSent);
    }

    [Fact]
    public async Task A_registration_whose_email_failed_says_that_instead_of_claiming_success()
    {
        var context = new AuthHandlerTestContext { EmailSender = RefusingSender() };

        var result = await new RegisterDealerOwnerHandler(context.Registrar)
            .Handle(Command(), CancellationToken.None);

        // The registration still succeeded...
        Assert.True(result.IsSuccess);
        Assert.Equal("owner@gallery.jo", result.Value.Email);
        // ...and the account is really there, with its token, ready for a resend.
        Assert.Single(context.AddedUsers);
        Assert.Single(context.AddedVerificationTokens);
        // ...but nobody is told an email is on its way.
        Assert.False(result.Value.VerificationEmailSent);
    }

    [Fact]
    public async Task A_resend_that_the_mail_server_refused_is_reported_as_a_failure()
    {
        var context = new AuthHandlerTestContext { EmailSender = RefusingSender() };
        var user = Users.Customer(verified: false);
        context.UserRepository.GetByEmailAsync(user.Email, Arg.Any<CancellationToken>()).Returns(user);

        var result = await context.ResendVerification()
            .Handle(new ResendVerificationCommand(user.Email.Value), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("auth.verification_email_not_sent", result.Error.Code);
    }

    /// <summary>
    /// The anti-enumeration answer survives. An address nobody registered gets the same success as a
    /// real resend, because the alternative turns this endpoint into a way to ask the platform who
    /// holds an account. Only a send that was ATTEMPTED and refused reports a failure.
    /// </summary>
    [Fact]
    public async Task An_unknown_address_still_gets_the_same_answer_as_a_real_one()
    {
        var context = new AuthHandlerTestContext { EmailSender = RefusingSender() };
        context.UserRepository.GetByEmailAsync(Arg.Any<EmailAddress>(), Arg.Any<CancellationToken>())
            .Returns((User?)null);

        var result = await context.ResendVerification()
            .Handle(new ResendVerificationCommand("nobody@example.com"), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }
    /// <summary>
    /// The screen behind this one said "a reset link is on its way" whatever the relay answered.
    ///
    /// On 2026-09-06 that was not hypothetical: two resets for a real address failed at the relay,
    /// fifteen seconds each, and both times the browser was told to go and check its inbox.
    /// </summary>
    [Fact]
    public async Task A_reset_link_the_mail_server_refused_is_reported_as_a_failure()
    {
        var context = new AuthHandlerTestContext { EmailSender = RefusingSender() };
        var user = Users.Customer();
        context.UserRepository.GetByEmailAsync(user.Email, Arg.Any<CancellationToken>()).Returns(user);

        var result = await context.ForgotPassword()
            .Handle(new ForgotPasswordCommand(user.Email.Value), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("auth.password_reset_email_not_sent", result.Error.Code);
        // The token was still issued, so trying again costs the person nothing.
        Assert.Single(context.AddedVerificationTokens);
    }

    /// <summary>
    /// And the oracle stays shut for the case that matters: an address with no account gets the same
    /// success as a real one even while every send is failing. Only an ATTEMPTED send reports.
    /// </summary>
    [Fact]
    public async Task An_unknown_address_asking_for_a_reset_gets_the_same_answer_as_a_real_one()
    {
        var context = new AuthHandlerTestContext { EmailSender = RefusingSender() };
        context.UserRepository.GetByEmailAsync(Arg.Any<EmailAddress>(), Arg.Any<CancellationToken>())
            .Returns((User?)null);

        var result = await context.ForgotPassword()
            .Handle(new ForgotPasswordCommand("nobody@example.com"), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }
}
