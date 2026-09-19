using System.Net;
using System.Text.Json;
using Khadra.Application.Common.Ports;
using Khadra.Infrastructure.Configuration;
using Khadra.Infrastructure.Notifications;
using Khadra.Tests.Support;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Khadra.Tests.Infrastructure;

/// <summary>
/// The Brevo transport against a stubbed endpoint: what it sends, and what its receipt claims.
/// </summary>
/// <remarks>
/// Brevo is the transport registrations from the phone go through, and it had no test of its own. The
/// receipt is the part that matters most: Brevo's <c>messageId</c> is the only thing that joins a line
/// in the API's log to an entry in Brevo's Transactional log, where delivery is recorded. A receipt
/// must never claim more than Brevo said, and must never fail a message Brevo accepted.
/// </remarks>
public sealed class BrevoEmailSenderTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 9, 30, 0, TimeSpan.Zero);

    /// <summary>Captures the outgoing request and answers with whatever the test asks for.</summary>
    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            RequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(body) };
        }
    }

    private static (BrevoEmailSender Sender, StubHandler Handler) Build(HttpStatusCode status, string body)
    {
        var handler = new StubHandler(status, body);
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(BrevoEmailSender.HttpClientName)
            .Returns(_ => new HttpClient(handler, disposeHandler: false)
            {
                BaseAddress = new Uri("https://api.brevo.com/"),
            });

        var options = Options.Create(new EmailOptions
        {
            Provider = EmailOptions.BrevoProvider,
            ApiKey = "xkeysib-not-a-real-key",
            FromAddress = "no-reply@khadra.test",
            FromName = "Khadra",
        });

        return (new BrevoEmailSender(factory, options, new TestClock(Now)), handler);
    }

    private static EmailMessage Message() =>
        new("ali@example.com", "Ali Haddad", "Verify your Khadra email address", "<p>link</p>", "link");

    [Fact]
    public async Task An_accepted_message_is_posted_and_its_receipt_carries_brevos_message_id()
    {
        var (sender, handler) = Build(
            HttpStatusCode.Created,
            """{"messageId":"<202609170930.12345678901@smtp-relay.mailin.fr>"}""");

        var receipt = await sender.SendAsync(Message());

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("https://api.brevo.com/v3/smtp/email", handler.Request.RequestUri!.ToString());
        using (var body = JsonDocument.Parse(handler.RequestBody!))
        {
            Assert.Equal("no-reply@khadra.test", body.RootElement.GetProperty("sender").GetProperty("email").GetString());
            Assert.Equal("ali@example.com", body.RootElement.GetProperty("to")[0].GetProperty("email").GetString());
        }

        Assert.Equal("Brevo", receipt.Provider);
        Assert.Equal("<202609170930.12345678901@smtp-relay.mailin.fr>", receipt.ProviderMessageId);
        Assert.Equal(Now, receipt.AcceptedAt);
        Assert.Equal(1, receipt.Attempts);
        Assert.Null(receipt.ProviderResponse);
    }

    /// <summary>
    /// Brevo said yes, then said something nobody can read. The message still went, so the send still
    /// succeeds: the log loses an id, and a registration screen does not tell somebody that an email
    /// failed which is already in their inbox.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{"messageId":42}""")]
    [InlineData("""["<a@b>"]""")]
    public async Task An_accepted_message_whose_answer_cannot_be_read_is_still_accepted(string answer)
    {
        var (sender, _) = Build(HttpStatusCode.Created, answer);

        var receipt = await sender.SendAsync(Message());

        Assert.Equal("Brevo", receipt.Provider);
        Assert.Null(receipt.ProviderMessageId);
    }

    /// <summary>A line break inside an id is how one log line becomes two.</summary>
    [Fact]
    public async Task A_message_id_cannot_carry_a_line_break_into_the_log()
    {
        var (sender, _) = Build(HttpStatusCode.Created, """{"messageId":"<a@b>\nforged: line"}""");

        var receipt = await sender.SendAsync(Message());

        Assert.DoesNotContain("\n", receipt.ProviderMessageId, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_refused_message_throws_with_brevos_own_explanation()
    {
        var (sender, _) = Build(
            HttpStatusCode.BadRequest,
            """{"code":"invalid_parameter","message":"sender is not valid"}""");

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync(Message()));

        Assert.Contains("sender is not valid", thrown.Message, StringComparison.Ordinal);
        Assert.Contains("400", thrown.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A refusal becomes an exception that is logged at Error, and a refusal is where a provider names
    /// mailboxes. The reason survives; the address does not.
    /// </summary>
    [Fact]
    public async Task A_refusal_keeps_its_reason_and_loses_the_address_it_quoted()
    {
        var (sender, _) = Build(
            HttpStatusCode.BadRequest,
            """{"code":"invalid_parameter","message":"email is not valid: ali@example.com"}""");

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync(Message()));

        Assert.Contains("email is not valid", thrown.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("ali@example.com", thrown.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A proxy's error page in front of Brevo does not become a page of log.</summary>
    [Fact]
    public async Task A_refusal_the_size_of_a_web_page_is_one_short_line()
    {
        var page = string.Concat(Enumerable.Repeat("<p>502 Bad Gateway</p>\n", 300));
        var (sender, _) = Build(HttpStatusCode.BadGateway, page);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync(Message()));

        Assert.DoesNotContain("\n", thrown.Message, StringComparison.Ordinal);
        Assert.True(
            thrown.Message.Length <= ProviderReply.MaxRefusalLength + 100,
            $"the refusal was {thrown.Message.Length} characters long");
    }
}
