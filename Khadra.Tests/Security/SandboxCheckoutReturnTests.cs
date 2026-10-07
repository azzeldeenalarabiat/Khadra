using System.Text.Json;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Domain.Common;
using Khadra.Domain.Payments;
using Khadra.Domain.Payments.Repositories;
using Khadra.Infrastructure.Configuration;
using Khadra.Infrastructure.Payments;
using Khadra.Tests.Support;
using Khadra.WebAPI;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Khadra.Tests.Security;

/// <summary>
/// The sandbox checkout hands the customer back to their booking, the way a hosted checkout does.
/// </summary>
/// <remarks>
/// <para>
/// It did not. Every outcome was delivered and the page simply stayed open, so a tester on Staging
/// finished each payment by pressing Back — and the customer website's booking page, which is where
/// the result of the webhook is actually shown, was never where the flow ended.
/// </para>
/// <para>
/// What is pinned is WHERE it returns: to <see cref="IPaymentSettings.ReturnUrlFor"/> for the
/// payment's booking, which is the address <c>OpenCheckout</c> gives every provider, so the sandbox
/// cannot drift from what a real one would do. It is driven through the real
/// <see cref="PaymentSettings"/> built from configuration, because the bug a substitute would hide
/// is a page returning somewhere the configuration never said. The handler is called directly
/// rather than booted: a test host cannot run the sandbox, whose data guard refuses the unreachable
/// database every test host is given (see <c>SandboxPaymentGuardTests</c>).
/// </para>
/// </remarks>
public sealed class SandboxCheckoutReturnTests
{
    private const string Reference = "sbx_0123456789abcdef";
    private const string Website = "https://customer.example";
    private const string Console = "https://console.example";

    private static readonly IClock Clock = new TestClock(Build.Now);

    private static readonly IOptions<PaymentOptions> Secret =
        Options.Create(new PaymentOptions { WebhookSecret = "sandbox-page-test-secret" });

    private static Payment OpenPayment() =>
        Payment.Open(Id.New(), Id.New(), Money.Jod(18m), PaymentProviders.Sandbox, Build.Now.AddMinutes(30), Build.Now);

    private static PaymentSettings Settings(string returnUrlBase) =>
        new PaymentSettings(
            Options.Create(new PaymentOptions { ReturnUrlBase = returnUrlBase }),
            Options.Create(new AppOptions { ClientBaseUrl = Console }));

    private static async Task<string> PageFor(Payment payment, IPaymentSettings settings, string? lang = null)
    {
        var payments = Substitute.For<IPaymentRepository>();
        payments.GetByProviderReferenceAsync(PaymentProviders.Sandbox, Reference, Arg.Any<CancellationToken>())
            .Returns(payment);

        var result = await SandboxCheckoutEndpoints.ShowAsync(Reference, lang, payments, settings, Secret, Clock, CancellationToken.None);

        return Assert.IsType<ContentHttpResult>(result).ResponseContent!;
    }

    [Fact]
    public async Task The_checkout_returns_to_the_booking_on_the_configured_return_address()
    {
        var payment = OpenPayment();

        var page = await PageFor(payment, Settings(Website), "en");

        Assert.Contains($"""<a id="back" href="{Website}/en/bookings/{payment.BookingId.Value}">""", page, StringComparison.Ordinal);
        Assert.DoesNotContain(Console, page, StringComparison.Ordinal);
    }

    /// <summary>
    /// Back in the language the checkout was opened in (Fix & Polish Wave 3, E3; E2E F25: a customer who paid from the
    /// Arabic site came back to the English one). Only one of the platform's two languages is taken from the link;
    /// anything else is the default, and the host and the path never come from it.
    /// </summary>
    [Theory]
    [InlineData("ar", "ar")]
    [InlineData("en", "en")]
    [InlineData(null, "en")]
    [InlineData("fr", "en")]
    [InlineData("ar/../../evil.example", "en")]
    [InlineData("AR", "en")]
    public async Task The_checkout_returns_in_the_language_it_was_opened_in_and_nothing_else_from_the_link(string? lang, string expected)
    {
        var payment = OpenPayment();

        var page = await PageFor(payment, Settings(Website), lang);

        Assert.Contains($"""<a id="back" href="{Website}/{expected}/bookings/{payment.BookingId.Value}">""", page, StringComparison.Ordinal);
        Assert.DoesNotContain("evil.example", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// Whatever the configuration, it is the address the provider itself was handed — including the
    /// fallback when no return address is configured, which is the existing behaviour of the port.
    /// </summary>
    [Theory]
    [InlineData(Website)]
    [InlineData(Website + "/")]
    [InlineData("")]
    public async Task The_checkout_returns_exactly_where_the_provider_was_told_to(string returnUrlBase)
    {
        var payment = OpenPayment();
        var settings = Settings(returnUrlBase);

        var page = await PageFor(payment, settings);

        Assert.Contains(
            $"""href="{settings.ReturnUrlFor(payment.BookingId, Language.Default).AbsoluteUri}">""",
            page,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// It leaves only once the webhook has ACCEPTED the delivery, and the replay button never leaves.
    /// </summary>
    /// <remarks>
    /// The script is asserted as text because the suite runs no browser; the behaviour itself is
    /// checked by driving the page. What this catches is the redirect being moved in front of the
    /// webhook's answer, or onto the button that exists to be pressed again.
    /// </remarks>
    [Fact]
    public async Task The_checkout_leaves_only_after_the_webhook_accepts_an_outcome()
    {
        var page = await PageFor(OpenPayment(), Settings(Website));

        Assert.Contains("return response.ok;", page, StringComparison.Ordinal);
        Assert.Contains("if (accepted) returnToBooking();", page, StringComparison.Ordinal);
        Assert.Contains("""onclick="send()">Deliver the last one again""", page, StringComparison.Ordinal);
        Assert.Contains("location.replace(back)", page, StringComparison.Ordinal);
        // The address lives in the link alone, so the script needs no encoding of its own.
        var script = page[page.IndexOf("<script>", StringComparison.Ordinal)..];
        Assert.DoesNotContain(Website, script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_return_address_is_html_encoded()
    {
        var payment = OpenPayment();

        var page = await PageFor(payment, Settings("https://customer.example/a&b"));

        Assert.Contains($"""href="https://customer.example/a&amp;b/en/bookings/{payment.BookingId.Value}">""", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// Each refund the sweep has SENT gets its own Settle and Fail buttons naming it (Phase 3), the
    /// reference HTML-encoded into the markup and never spliced into the script; one not sent yet says
    /// so. A refund event reloads the console, so the new status is what the tester sees.
    /// </summary>
    [Fact]
    public async Task Every_sent_refund_gets_its_own_buttons_naming_it()
    {
        var payment = OpenPayment();
        Assert.True(payment.AttachProviderSession(Reference, "https://console.example/x").IsSuccess);
        Assert.True(payment.Orphan(Money.Jod(18m), Build.Now, "BookingExpired", Build.Now).IsSuccess);
        var sent = Assert.Single(payment.Refunds);
        sent.MarkSent("sbxrf_a<b", Build.Now);

        var page = await PageFor(payment, Settings(Website));

        Assert.Contains("""data-ref="sbxrf_a&lt;b" data-amount="18.000" data-kind="refund_settled">Settle this refund""", page, StringComparison.Ordinal);
        Assert.Contains("""data-ref="sbxrf_a&lt;b" data-amount="18.000" data-kind="refund_failed">Fail this refund""", page, StringComparison.Ordinal);
        Assert.Contains("if (accepted && isRefund) setTimeout(() => location.reload(), 1500);", page, StringComparison.Ordinal);
        var script = page[page.IndexOf("<script>", StringComparison.Ordinal)..];
        Assert.DoesNotContain("sbxrf_a", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_refund_not_sent_yet_has_no_buttons_and_says_why()
    {
        var payment = OpenPayment();
        Assert.True(payment.AttachProviderSession(Reference, "https://console.example/x").IsSuccess);
        Assert.True(payment.Orphan(Money.Jod(18m), Build.Now, "BookingExpired", Build.Now).IsSuccess);

        var page = await PageFor(payment, Settings(Website));

        Assert.Contains("Not sent yet", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Settle this refund", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_reference_is_still_a_bare_404()
    {
        var payments = Substitute.For<IPaymentRepository>();

        var result = await SandboxCheckoutEndpoints.ShowAsync("sbx_unknown", "ar", payments, Settings(Website), Secret, Clock, CancellationToken.None);

        Assert.IsType<NotFound>(result);
    }

    /// <summary>
    /// The page offers the session's capture reference (Wave 4, B1), written into the field's value and read back from
    /// it by the script, never spliced into the script: left alone, pressing Pay again is the same capture said again.
    /// </summary>
    [Fact]
    public async Task The_page_offers_the_sessions_capture_reference_in_its_own_field()
    {
        var page = await PageFor(OpenPayment(), Settings(Website), "en");

        var expected = SandboxEvents.CaptureReferenceFor(Reference, Secret.Value.WebhookSecret);
        Assert.Contains($"""<input id="capture" type="text" value="{expected}">""", page, StringComparison.Ordinal);
        Assert.Contains("document.getElementById('capture').value.trim()", page, StringComparison.Ordinal);
        var script = page[page.IndexOf("<script>", StringComparison.Ordinal)..];
        Assert.DoesNotContain(expected, script, StringComparison.Ordinal);
    }

    /// <summary>
    /// What the page's Pay button sends: the capture reference the tester left, edited or cleared, trimmed, on a
    /// capture and on nothing else.
    /// </summary>
    [Theory]
    [InlineData("captured", " sbxcap_edited ", "sbxcap_edited")]
    [InlineData("captured", "   ", null)]
    [InlineData("captured", null, null)]
    [InlineData("failed", "sbxcap_edited", null)]
    [InlineData("refund_settled", "sbxcap_edited", null)]
    public async Task Only_a_capture_sent_from_the_page_carries_a_capture_reference(string kind, string? typed, string? expected)
    {
        var payments = Substitute.For<IPaymentRepository>();
        payments.GetByProviderReferenceAsync(PaymentProviders.Sandbox, Reference, Arg.Any<CancellationToken>())
            .Returns(OpenPayment());

        var result = await SandboxCheckoutEndpoints.BuildEventAsync(
            Reference,
            new SandboxCheckoutEndpoints.SandboxOutcome("evt_page", kind, null, null, CaptureReference: typed),
            payments,
            Secret,
            Clock,
            CancellationToken.None);

        var envelope = Assert.IsAssignableFrom<IValueHttpResult>(result).Value;
        using var signed = JsonDocument.Parse(JsonSerializer.Serialize(envelope));
        using var body = JsonDocument.Parse(signed.RootElement.GetProperty("body").GetString()!);
        var sent = body.RootElement.TryGetProperty("captureReference", out var field) && field.ValueKind == JsonValueKind.String
            ? field.GetString()
            : null;
        Assert.Equal(expected, sent);
    }
}
