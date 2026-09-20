using Khadra.Application.Common.Ports;
using NSubstitute;

namespace Khadra.Tests.Support;

/// <summary>
/// Mail transports for handler tests.
/// </summary>
/// <remarks>
/// A bare <c>Substitute.For&lt;IEmailSender&gt;()</c> answers every send with a NULL receipt — a sealed
/// record is not something NSubstitute can invent — and the dispatchers read the receipt to log it. A
/// test that means "the transport accepted it" builds its transport here.
/// </remarks>
internal static class TestEmail
{
    public static readonly DateTimeOffset AcceptedAt = new(2026, 9, 17, 8, 0, 0, TimeSpan.Zero);

    public static EmailSendReceipt Accepted() =>
        new("Test", "<accepted@khadra.test>", AcceptedAt, Attempts: 1, ProviderResponse: null);

    /// <summary>A transport that accepts every message it is handed.</summary>
    public static IEmailSender AcceptingSender()
    {
        var sender = Substitute.For<IEmailSender>();
        sender.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>()).Returns(Accepted());
        return sender;
    }
}
