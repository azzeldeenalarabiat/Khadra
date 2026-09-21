using Khadra.Application.Common.Ports;
using Khadra.Domain.Common;
using Khadra.Domain.Payments;
using Khadra.Infrastructure.Configuration;
using Khadra.Infrastructure.Payments;
using Khadra.Tests.Support;
using Microsoft.Extensions.Options;

namespace Khadra.Tests.Security;

/// <summary>
/// The sandbox adapter's own two halves, and the fact that they agree with each other.
/// </summary>
/// <remarks>
/// <para>
/// This provider both SIGNS the events and VERIFIES them, which is the whole reason it is testable
/// and also the reason it can be silently broken: the two halves can drift apart and each look
/// correct on its own. They did. <c>BuildEvent</c> wrote camelCase keys and <c>ParseEvent</c> read
/// PascalCase properties with case-SENSITIVE defaults, so every body this provider produced was one
/// it then refused — with a signature that verified perfectly, and a 401 that says nothing about
/// casing. No guard test would have caught it, because the guards never call either method, and no
/// handler test would, because they all substitute the port. Only a round trip does.
/// </para>
/// <para>
/// The signature half is tested for the property that matters on a reachable host: the webhook
/// endpoint is anonymous, so an unsigned or altered body must be refused. Otherwise a customer who
/// found the URL could confirm their own booking by posting a capture.
/// </para>
/// </remarks>
public sealed class SandboxPaymentProviderTests
{
    private const string Secret = "sandbox-secret-that-is-long-enough";
    private const string ConsoleBase = "http://192.0.2.10:5012";

    private static SandboxPaymentProvider Provider(string secret = Secret, string console = ConsoleBase) =>
        new(Options.Create(new PaymentOptions
        {
            Provider = PaymentProviders.Sandbox,
            WebhookSecret = secret,
            SandboxConsoleBaseUrl = console,
        }),
        new TestClock(Build.Now));

    private static Dictionary<string, string> Headers(string signature) =>
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [SandboxEvents.SignatureHeader] = signature,
            ["content-type"] = "application/json",
        };

    // ------------------------------------------------------------------ the round trip

    /// <summary>
    /// A capture this provider builds is one it reads back whole, amount and all.
    /// </summary>
    /// <remarks>
    /// The amount is the field the round trip really has to prove, because it does not survive
    /// unchanged: it travels in MINOR units, as every real provider sends it, so 75.000 JOD crosses
    /// as 75000 and has to come back as 75.000 rather than as 75 or 75000. The deposit comparison in
    /// <c>ReceiveProviderEventHandler</c> is what decides whether a booking confirms or the money is
    /// orphaned and refunded, so a factor of a thousand here is a customer whose paid booking is
    /// refunded.
    /// </remarks>
    [Fact]
    public void A_capture_survives_the_round_trip()
    {
        var provider = Provider();
        var amount = Money.Jod(75.500m);
        var (body, signature) = SandboxEvents.Build(
            "evt_1", "sbx_abc", "captured", amount, Secret);

        var parsed = provider.ParseEvent(body, Headers(signature));

        Assert.True(parsed.IsSuccess);
        Assert.Equal("evt_1", parsed.Value.ProviderEventId);
        Assert.Equal("sbx_abc", parsed.Value.ProviderReference);
        Assert.Equal(ProviderEventKind.Captured, parsed.Value.Kind);
        Assert.NotNull(parsed.Value.Amount);
        Assert.Equal(75.500m, parsed.Value.Amount!.Amount);
        Assert.Equal("JOD", parsed.Value.Amount.CurrencyCode);
    }

    /// <summary>Three fils is a real amount, and the one an exponent mistake destroys.</summary>
    [Theory]
    [InlineData("0.001")]
    [InlineData("0.750")]
    [InlineData("75.000")]
    [InlineData("1234.567")]
    public void Every_amount_comes_back_as_the_same_amount(string written)
    {
        var amount = Money.Jod(decimal.Parse(written, System.Globalization.CultureInfo.InvariantCulture));
        var (body, signature) = SandboxEvents.Build(
            "evt_amount", "sbx_abc", "captured", amount, Secret);

        var parsed = Provider().ParseEvent(body, Headers(signature));

        Assert.True(parsed.IsSuccess);
        Assert.Equal(amount.Amount, parsed.Value.Amount!.Amount);
    }

    /// <summary>A refusal carries its code and no amount, which is how the platform records it.</summary>
    [Fact]
    public void A_failure_survives_the_round_trip_with_its_code()
    {
        var (body, signature) = SandboxEvents.Build(
            "evt_2", "sbx_abc", "failed", amount: null, Secret, failureCode: "card_declined");

        var parsed = Provider().ParseEvent(body, Headers(signature));

        Assert.True(parsed.IsSuccess);
        Assert.Equal(ProviderEventKind.Failed, parsed.Value.Kind);
        Assert.Equal("card_declined", parsed.Value.FailureCode);
        Assert.Null(parsed.Value.Amount);
    }

    [Theory]
    [InlineData("captured", ProviderEventKind.Captured)]
    [InlineData("failed", ProviderEventKind.Failed)]
    [InlineData("refund_settled", ProviderEventKind.RefundSettled)]
    [InlineData("refund_failed", ProviderEventKind.RefundFailed)]
    [InlineData("something_else", ProviderEventKind.Other)]
    public void Every_kind_maps_to_the_one_the_platform_acts_on(string kind, ProviderEventKind expected)
    {
        var (body, signature) = SandboxEvents.Build(
            "evt_kind", "sbx_abc", kind, amount: null, Secret);

        var parsed = Provider().ParseEvent(body, Headers(signature));

        Assert.True(parsed.IsSuccess);
        Assert.Equal(expected, parsed.Value.Kind);
    }

    // ------------------------------------------------------------------ the signature

    /// <summary>One changed byte and the body is refused. The endpoint is anonymous; this is the lock.</summary>
    [Fact]
    public void A_tampered_body_is_refused()
    {
        var (body, signature) = SandboxEvents.Build(
            "evt_3", "sbx_abc", "captured", Money.Jod(75m), Secret);
        var tampered = body.Replace("sbx_abc", "sbx_xyz", StringComparison.Ordinal);

        var parsed = Provider().ParseEvent(tampered, Headers(signature));

        Assert.True(parsed.IsFailure);
        Assert.Equal(PaymentErrors.UntrustedEvent.Code, parsed.Error.Code);
    }

    /// <summary>A body signed with somebody else's secret is somebody else's body.</summary>
    [Fact]
    public void A_body_signed_with_the_wrong_secret_is_refused()
    {
        var (body, signature) = SandboxEvents.Build(
            "evt_4", "sbx_abc", "captured", Money.Jod(75m), "a-different-secret");

        var parsed = Provider().ParseEvent(body, Headers(signature));

        Assert.True(parsed.IsFailure);
    }

    [Fact]
    public void A_body_with_no_signature_header_is_refused()
    {
        var (body, _) = SandboxEvents.Build(
            "evt_5", "sbx_abc", "captured", Money.Jod(75m), Secret);

        var parsed = Provider().ParseEvent(
            body,
            new Dictionary<string, string> { ["content-type"] = "application/json" });

        Assert.True(parsed.IsFailure);
    }

    /// <summary>
    /// A body that is not JSON at all is refused rather than thrown over.
    /// </summary>
    /// <remarks>
    /// Correctly signed, deliberately: the point is that the parse step after verification is also
    /// total. An exception here would answer 500, and a real provider reads 5xx as "retry" — so a
    /// malformed delivery would be offered forever.
    /// </remarks>
    [Fact]
    public void A_signed_body_that_is_not_json_is_refused_rather_than_thrown()
    {
        const string body = "this is not json";

        var parsed = Provider().ParseEvent(
            body, Headers(SandboxEvents.Sign(body, Secret)));

        Assert.True(parsed.IsFailure);
    }

    /// <summary>
    /// And neither is a negative amount or a nonsense currency, which <c>Money</c> would throw over.
    /// </summary>
    /// <remarks>
    /// <c>Money.Create</c> raises <c>DomainException</c> for both, and this body is untrusted input.
    /// Checked rather than caught, so the webhook answers 401 and the provider stops rather than
    /// answering 500 and being retried until the endpoint is disabled.
    /// </remarks>
    [Theory]
    [InlineData(-1, "JOD")]
    [InlineData(1000, "JODX")]
    [InlineData(1000, "J")]
    public void A_signed_body_with_an_impossible_amount_is_refused(long minor, string currency)
    {
        var body = $$"""
            {"eventId":"evt_6","reference":"sbx_abc","kind":"captured","amountMinor":{{minor}},"currency":"{{currency}}"}
            """;

        var parsed = Provider().ParseEvent(body, Headers(SandboxEvents.Sign(body, Secret)));

        Assert.True(parsed.IsFailure);
    }

    // ------------------------------------------------------------------ the checkout

    /// <summary>
    /// The checkout URL is absolute, on the configured console address, and unguessable.
    /// </summary>
    /// <remarks>
    /// Absolute because a phone opens it in its own browser. Unguessable because the reference it
    /// carries is the capability a signed capture is posted against, so deriving it from a booking id
    /// would let anyone who knows a booking confirm it.
    /// </remarks>
    [Fact]
    public async Task A_checkout_opens_on_an_absolute_unguessable_url()
    {
        var session = await Provider().CreateCheckoutAsync(Request());

        Assert.True(session.IsSuccess);
        var url = new Uri(session.Value.CheckoutUrl, UriKind.Absolute);
        Assert.Equal("192.0.2.10", url.Host);
        Assert.StartsWith(SandboxEvents.ConsolePath, url.AbsolutePath, StringComparison.Ordinal);
        Assert.StartsWith("sbx_", session.Value.ProviderReference, StringComparison.Ordinal);
        // Nothing from the request. Not the payment id, not the booking reference.
        Assert.DoesNotContain("KHD", session.Value.ProviderReference, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Two checkouts never mint the same reference.</summary>
    [Fact]
    public async Task Each_checkout_gets_its_own_reference()
    {
        var provider = Provider();

        var first = await provider.CreateCheckoutAsync(Request());
        var second = await provider.CreateCheckoutAsync(Request());

        Assert.NotEqual(first.Value.ProviderReference, second.Value.ProviderReference);
    }

    /// <summary>
    /// And a sweep asking about a forgotten session is told it failed, not that the provider is down.
    /// </summary>
    /// <remarks>
    /// This provider keeps no session state. Answering "unavailable" would leave every abandoned
    /// attempt open forever — one logged failure per row per one-minute tick — and the customer could
    /// not open a fresh checkout inside their own window. Failed is also the true answer: nothing was
    /// captured. A late capture posted afterwards still lands and is orphaned, exactly as a real one
    /// would be.
    /// </remarks>
    [Fact]
    public async Task A_forgotten_session_is_reported_as_failed()
    {
        var state = await Provider().QueryAsync("sbx_forgotten");

        Assert.True(state.IsSuccess);
        Assert.Equal(ProviderEventKind.Failed, state.Value.Kind);
        Assert.Equal("sandbox_session_forgotten", state.Value.FailureCode);
    }

    /// <summary>It reports Sandbox, which is the one fact every client is told.</summary>
    [Fact]
    public void It_reports_the_sandbox_mode_and_its_own_name()
    {
        var provider = Provider();

        Assert.Equal(PaymentMode.Sandbox, provider.Mode);
        Assert.Equal(PaymentProviders.Sandbox, provider.Name);
        // True, because the Pay button genuinely works. The MODE is what says it is not real.
        Assert.True(((IPaymentProvider)provider).IsConfigured);
    }

    private static CheckoutRequest Request() => new(
        Id.New(),
        Money.Jod(75m),
        "KHD-2026-0001",
        "customer@example.com",
        Build.Now.AddMinutes(30),
        new Uri("https://app.example.com/bookings/1"));
}
