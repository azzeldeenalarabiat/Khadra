using Khadra.Application.Common.Ports;
using Khadra.Infrastructure.Notifications;
using Khadra.Tests.Support;

namespace Khadra.Tests.Infrastructure;

/// <summary>
/// The transport that delivers nothing must not become the place the credentials go.
/// </summary>
/// <remarks>
/// It wrote each message's whole text body to the log, and the body of every auth email is a link with
/// a one-time token in it: a verification link, a password reset, a seven-day administrator invitation.
/// Pre-launch item 85 records the production days that put live ones in the hosting provider's log.
/// </remarks>
public sealed class LoggingEmailSenderTests
{
    private const string Token = "tok-9f41c2d7e8b3a605";
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 9, 30, 0, TimeSpan.Zero);

    private static EmailMessage Invitation() => new(
        "rania.haddad@example.jo",
        "Rania Haddad",
        "You have been invited to administer Khadra",
        $"<a href=\"https://console.khadra.test/accept-invitation?token={Token}\">Accept</a>",
        $"Accept: https://console.khadra.test/accept-invitation?token={Token}");

    [Fact]
    public async Task It_logs_that_nothing_was_delivered_without_the_link_the_token_or_the_address()
    {
        var log = new RecordingLogger<LoggingEmailSender>();

        await new LoggingEmailSender(new TestClock(Now), log).SendAsync(Invitation());

        var line = Assert.Single(log.Entries);
        Assert.Contains("NOT delivered", line.Message, StringComparison.Ordinal);
        Assert.Contains("You have been invited to administer Khadra", line.Message, StringComparison.Ordinal);
        Assert.Contains("example.jo", line.Message, StringComparison.Ordinal);

        Assert.DoesNotContain(Token, log.AllText, StringComparison.Ordinal);
        Assert.DoesNotContain("accept-invitation", log.AllText, StringComparison.Ordinal);
        Assert.DoesNotContain("rania.haddad", log.AllText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Its_receipt_says_the_message_was_not_delivered()
    {
        var receipt = await new LoggingEmailSender(new TestClock(Now), new RecordingLogger<LoggingEmailSender>())
            .SendAsync(Invitation());

        Assert.Equal("Logging", receipt.Provider);
        Assert.Null(receipt.ProviderMessageId);
        Assert.Equal(Now, receipt.AcceptedAt);
        Assert.Contains("not delivered", receipt.ProviderResponse, StringComparison.Ordinal);
    }
}
