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
/// The Resend transport, tested against a stubbed HTTP endpoint rather than the real one.
///
/// What matters is the shape of the request and, above all, what happens when the provider REFUSES:
/// the send has to fail loudly enough for `AuthEmailDispatcher` to report it, because the whole point
/// of that plumbing is that the console never claims an email was sent when it was not.
/// </summary>
public sealed class ResendEmailSenderTests
{
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

    private static (ResendEmailSender Sender, StubHandler Handler) Build(
        HttpStatusCode status,
        string body = "{}",
        string? apiKey = "re_test_key",
        string? fromAddress = "onboarding@resend.dev")
    {
        var handler = new StubHandler(status, body);
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>())
            .Returns(_ => new HttpClient(handler, disposeHandler: false)
            {
                BaseAddress = new Uri("https://api.resend.com/"),
            });

        var options = Options.Create(new EmailOptions
        {
            Provider = EmailOptions.ResendProvider,
            ApiKey = apiKey,
            FromAddress = fromAddress,
            FromName = "Khadra",
        });

        return (new ResendEmailSender(factory, options, new TestClock(Now)), handler);
    }

    private static readonly DateTimeOffset Now = new(2026, 9, 17, 9, 30, 0, TimeSpan.Zero);

    private static EmailMessage Message() =>
        new("owner@gallery.jo", "Rami Odeh", "Verify your Khadra email address", "<p>link</p>", "link");

    [Fact]
    public async Task An_accepted_message_is_posted_with_the_key_and_the_sender()
    {
        var (sender, handler) = Build(HttpStatusCode.OK, """{"id":"abc-123"}""");

        var receipt = await sender.SendAsync(Message());

        // Resend's id is what its email log is searched by. The receipt claims acceptance and no more.
        Assert.Equal("Resend", receipt.Provider);
        Assert.Equal("abc-123", receipt.ProviderMessageId);
        Assert.Equal(Now, receipt.AcceptedAt);
        Assert.Equal(1, receipt.Attempts);

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("https://api.resend.com/emails", handler.Request.RequestUri!.ToString());
        Assert.Equal("Bearer", handler.Request.Headers.Authorization!.Scheme);
        Assert.Equal("re_test_key", handler.Request.Headers.Authorization.Parameter);

        using var body = JsonDocument.Parse(handler.RequestBody!);
        // "Name <address>", so the recipient sees the platform rather than a bare address.
        Assert.Equal("Khadra <onboarding@resend.dev>", body.RootElement.GetProperty("from").GetString());
        Assert.Equal("owner@gallery.jo", body.RootElement.GetProperty("to")[0].GetString());
        Assert.Equal("Verify your Khadra email address", body.RootElement.GetProperty("subject").GetString());
        // Both bodies travel: a text part is what stops the message looking like spam.
        Assert.Equal("<p>link</p>", body.RootElement.GetProperty("html").GetString());
        Assert.Equal("link", body.RootElement.GetProperty("text").GetString());
    }

    /// <summary>
    /// A refusal must throw. `AuthEmailDispatcher` catches it and reports the send as failed, which
    /// is what stops the registration screen promising an email nobody sent.
    /// </summary>
    [Fact]
    public async Task A_refused_message_throws_and_carries_the_providers_own_explanation()
    {
        var (sender, _) = Build(
            HttpStatusCode.Forbidden,
            """{"message":"The gmail.com domain is not verified."}""");

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync(Message()));

        // The operator needs Resend's sentence, not "email failed".
        Assert.Contains("not verified", thrown.Message, StringComparison.Ordinal);
        Assert.Contains("403", thrown.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Resend's free tier refuses by quoting the account owner's own mailbox, and the refusal is logged
    /// at Error. The reason is what an operator needs, and it survives without the address.
    /// </summary>
    [Fact]
    public async Task A_free_tier_refusal_does_not_carry_the_account_owners_address_out()
    {
        var (sender, _) = Build(
            HttpStatusCode.Forbidden,
            """{"statusCode":403,"message":"You can only send testing emails to your own email address (owner@example.com).","name":"validation_error"}""");

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync(Message()));

        Assert.Contains("You can only send testing emails", thrown.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("owner@example.com", thrown.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_missing_api_key_is_refused_before_any_call_is_made()
    {
        var (sender, handler) = Build(HttpStatusCode.OK, apiKey: null);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync(Message()));

        Assert.Contains("Email:ApiKey", thrown.Message, StringComparison.Ordinal);
        Assert.Null(handler.Request);
    }

    [Fact]
    public async Task A_missing_sender_is_refused_before_any_call_is_made()
    {
        var (sender, handler) = Build(HttpStatusCode.OK, fromAddress: null);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync(Message()));

        Assert.Contains("Email:FromAddress", thrown.Message, StringComparison.Ordinal);
        Assert.Null(handler.Request);
    }
}
