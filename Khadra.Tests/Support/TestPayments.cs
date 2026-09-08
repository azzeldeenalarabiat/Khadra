using CSharpFunctionalExtensions;
using Khadra.Application.Common.Ports;
using Khadra.Domain.Common;
using Khadra.Domain.Payments;
using NSubstitute;

namespace Khadra.Tests.Support;

/// <summary>
/// Payment provider substitutes for handler tests.
/// </summary>
/// <remarks>
/// <para>
/// A substitute at the PORT, never a class in the shipped assemblies. The production build has
/// exactly one implementation and it refuses everything; a second one that succeeded would be a lie
/// the database could not tell apart from a real payment, which is why
/// <c>UnconfiguredPaymentProvider</c> says nobody may add one.
/// </para>
/// <para>
/// A test double is a different thing: it lives here, it is never registered in the container, and
/// nothing outside a test can reach it.
/// </para>
/// </remarks>
internal static class TestPayments
{
    public const string TestProviderName = "TestProvider";

    /// <summary>A provider that is not there, which is what the shipped build has.</summary>
    public static IPaymentProvider NoProvider()
    {
        var provider = Substitute.For<IPaymentProvider>();
        provider.Name.Returns(TestProviderName);
        provider.IsConfigured.Returns(false);
        provider.CreateCheckoutAsync(Arg.Any<CheckoutRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<CheckoutSession, Error>(PaymentErrors.ProviderUnavailable));
        provider.ParseEvent(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>())
            .Returns(Result.Failure<ProviderEvent, Error>(PaymentErrors.UntrustedEvent));
        return provider;
    }

    /// <summary>A provider that answers, for the paths that only exist once one does.</summary>
    public static IPaymentProvider Working(string providerReference = "sess_1")
    {
        var provider = Substitute.For<IPaymentProvider>();
        provider.Name.Returns(TestProviderName);
        provider.IsConfigured.Returns(true);
        provider.CreateCheckoutAsync(Arg.Any<CheckoutRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<CheckoutSession, Error>(
                new CheckoutSession(providerReference, $"https://provider.test/{providerReference}")));
        provider.RefundAsync(Arg.Any<RefundRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<ProviderRefund, Error>(new ProviderRefund("ref_1")));
        return provider;
    }

    public static IPaymentSettings Settings(
        TimeSpan? sessionLifetime = null,
        TimeSpan? closesBeforeDeadline = null,
        TimeSpan? staleGrace = null)
    {
        var settings = Substitute.For<IPaymentSettings>();
        settings.CheckoutSessionLifetime.Returns(sessionLifetime ?? TimeSpan.FromMinutes(30));
        settings.CheckoutClosesBeforeDeadline.Returns(closesBeforeDeadline ?? TimeSpan.FromMinutes(5));
        settings.StaleAttemptGrace.Returns(staleGrace ?? TimeSpan.FromMinutes(15));
        settings.ReturnUrlFor(Arg.Any<Id>())
            .Returns(call => new Uri($"https://app.test/bookings/{call.Arg<Id>().Value}"));
        return settings;
    }

    public static ProviderEvent Captured(
        string providerReference,
        Money amount,
        DateTimeOffset occurredAt,
        string eventId = "evt_1") =>
        new(eventId, providerReference, ProviderEventKind.Captured, amount, null, occurredAt);

    public static ProviderEvent Failed(
        string providerReference,
        DateTimeOffset occurredAt,
        string failureCode = "card_declined",
        string eventId = "evt_1") =>
        new(eventId, providerReference, ProviderEventKind.Failed, null, failureCode, occurredAt);
}
