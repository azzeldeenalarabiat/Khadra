using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Domain.Common;
using Khadra.Domain.Payments;
using Khadra.Domain.Payments.Repositories;
using Khadra.Infrastructure.Configuration;
using Khadra.Infrastructure.Payments;
using Khadra.Tests.Support;
using Khadra.WebAPI;
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

    private static Payment OpenPayment() =>
        Payment.Open(Id.New(), Id.New(), Money.Jod(18m), PaymentProviders.Sandbox, Build.Now.AddMinutes(30), Build.Now);

    private static PaymentSettings Settings(string returnUrlBase) =>
        new PaymentSettings(
            Options.Create(new PaymentOptions { ReturnUrlBase = returnUrlBase }),
            Options.Create(new AppOptions { ClientBaseUrl = Console }));

    private static async Task<string> PageFor(Payment payment, IPaymentSettings settings)
    {
        var payments = Substitute.For<IPaymentRepository>();
        payments.GetByProviderReferenceAsync(PaymentProviders.Sandbox, Reference, Arg.Any<CancellationToken>())
            .Returns(payment);

        var result = await SandboxCheckoutEndpoints.ShowAsync(Reference, payments, settings, Clock, CancellationToken.None);

        return Assert.IsType<ContentHttpResult>(result).ResponseContent!;
    }

    [Fact]
    public async Task The_checkout_returns_to_the_booking_on_the_configured_return_address()
    {
        var payment = OpenPayment();

        var page = await PageFor(payment, Settings(Website));

        Assert.Contains($"""<a id="back" href="{Website}/bookings/{payment.BookingId.Value}">""", page, StringComparison.Ordinal);
        Assert.DoesNotContain(Console, page, StringComparison.Ordinal);
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
            $"""href="{settings.ReturnUrlFor(payment.BookingId).AbsoluteUri}">""",
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

        Assert.Contains($"""href="https://customer.example/a&amp;b/bookings/{payment.BookingId.Value}">""", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_reference_is_still_a_bare_404()
    {
        var payments = Substitute.For<IPaymentRepository>();

        var result = await SandboxCheckoutEndpoints.ShowAsync("sbx_unknown", payments, Settings(Website), Clock, CancellationToken.None);

        Assert.IsType<NotFound>(result);
    }
}
