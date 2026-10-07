using Khadra.Domain.Common;
using Khadra.Domain.Payments;
using Khadra.Tests.Support;

namespace Khadra.Tests.Domain.Payments;

/// <summary>
/// A capture notice for money a payment has ALREADY taken (Wave 4, B1; E2E F30).
/// </summary>
/// <remarks>
/// Providers send several notices per charge under new event ids, so the replay index lets them through. Each one
/// used to be refused, fail to orphan, and be logged as money "UNACCOUNTED FOR" on a row whose money was right. Now
/// the payment says what the notice is — the same capture said again, or one it cannot account for — and changes
/// nothing either way.
/// </remarks>
public sealed class RepeatCaptureTests
{
    private static readonly DateTimeOffset Now = Build.Now;
    private static readonly Money Deposit = Money.Jod(18m);

    private static Payment Applied(string? captureReference)
    {
        var payment = Payment.Open(Id.New(), Id.New(), Deposit, "TestProvider", Now.AddMinutes(30), Now);
        payment.AttachProviderSession("sess_1", "https://provider.test/sess_1");
        Assert.True(payment.Apply(Money.Jod(18m), Now, Now, captureReference).IsSuccess);
        return payment;
    }

    private static Payment Orphaned(string? captureReference)
    {
        var payment = Payment.Open(Id.New(), Id.New(), Deposit, "TestProvider", Now.AddMinutes(30), Now);
        payment.AttachProviderSession("sess_1", "https://provider.test/sess_1");
        Assert.True(payment.Orphan(Money.Jod(18m), Now, "BookingExpired", Now, captureReference).IsSuccess);
        return payment;
    }

    [Fact]
    public void Applying_and_orphaning_both_keep_the_captures_reference()
    {
        Assert.Equal("cap_1", Applied("cap_1").ProviderCaptureReference);
        Assert.Equal("cap_1", Orphaned(" cap_1 ").ProviderCaptureReference);
        Assert.Null(Applied(null).ProviderCaptureReference);
        Assert.Null(Applied("   ").ProviderCaptureReference);
    }

    [Fact]
    public void The_same_reference_and_the_same_money_is_the_same_capture_said_again()
    {
        Assert.Same(ProviderEventOutcome.Duplicate, Applied("cap_1").ClassifyRepeatCapture(Money.Jod(18m), "cap_1"));
        // Orphaned money is captured money too: its repeat notice is the same capture, not a second refund's worth.
        Assert.Same(ProviderEventOutcome.Duplicate, Orphaned("cap_1").ClassifyRepeatCapture(Money.Jod(18m), "cap_1"));
    }

    /// <summary>The advisor's review: a same-reference notice must also agree on the money, or the provider contradicts itself.</summary>
    [Fact]
    public void The_same_reference_with_other_money_is_a_contradiction_not_a_duplicate()
    {
        var payment = Applied("cap_1");

        Assert.Same(ProviderEventOutcome.AmountMismatch, payment.ClassifyRepeatCapture(Money.Jod(20m), "cap_1"));
        Assert.Same(ProviderEventOutcome.AmountMismatch, payment.ClassifyRepeatCapture(Money.Create(18m, "USD"), "cap_1"));
    }

    [Fact]
    public void Another_reference_is_a_second_charge_whatever_the_amount()
    {
        var payment = Applied("cap_1");

        Assert.Same(ProviderEventOutcome.SecondCapture, payment.ClassifyRepeatCapture(Money.Jod(18m), "cap_2"));
        Assert.Same(ProviderEventOutcome.SecondCapture, payment.ClassifyRepeatCapture(Money.Jod(5m), "cap_2"));
    }

    /// <summary>
    /// Tier 2 needs a reference on BOTH sides. A payment captured before references existed, or a notice that carries
    /// none, is judged by the money alone, and a match is only ASSUMED to be the same capture.
    /// </summary>
    [Fact]
    public void Without_a_reference_on_both_sides_the_money_alone_decides_and_a_match_is_only_assumed()
    {
        var withReference = Applied("cap_1");
        var withoutReference = Applied(null);

        Assert.Same(ProviderEventOutcome.AssumedDuplicate, withReference.ClassifyRepeatCapture(Money.Jod(18m), null));
        Assert.Same(ProviderEventOutcome.AssumedDuplicate, withoutReference.ClassifyRepeatCapture(Money.Jod(18m), "cap_9"));
        Assert.Same(ProviderEventOutcome.AssumedDuplicate, withoutReference.ClassifyRepeatCapture(Money.Jod(18m), null));
        Assert.Same(ProviderEventOutcome.SecondCapture, withoutReference.ClassifyRepeatCapture(Money.Jod(19m), null));
        Assert.Same(ProviderEventOutcome.SecondCapture, withReference.ClassifyRepeatCapture(Money.Create(18m, "USD"), null));
    }

    /// <summary>Judging a repeat changes nothing: no status, no refund, no event.</summary>
    [Fact]
    public void Judging_a_repeat_notice_changes_nothing_on_the_payment()
    {
        var payment = Applied("cap_1");
        payment.ClearDomainEvents();

        payment.ClassifyRepeatCapture(Money.Jod(40m), "cap_2");

        Assert.Same(PaymentStatus.Applied, payment.Status);
        Assert.Empty(payment.Refunds);
        Assert.Empty(payment.DomainEvents);
        Assert.Equal(Money.Jod(18m), payment.AmountCaptured);
    }

    [Fact]
    public void Only_a_payment_that_took_money_can_be_asked_about_a_repeat()
    {
        var pending = Payment.Open(Id.New(), Id.New(), Deposit, "TestProvider", Now.AddMinutes(30), Now);

        Assert.Throws<DomainException>(() => pending.ClassifyRepeatCapture(Money.Jod(18m), "cap_1"));
    }

    [Fact]
    public void An_incident_names_the_other_attempt_exactly_when_it_is_a_capture_on_another_attempt()
    {
        Assert.Throws<DomainException>(() => PaymentIncident.Raise(
            PaymentIncidentKind.CaptureOnAnotherAttempt, Id.New(), Id.New(), "TestProvider", "cap_1",
            Money.Jod(18m), Money.Jod(18m), otherPaymentId: null, Now));
        Assert.Throws<DomainException>(() => PaymentIncident.Raise(
            PaymentIncidentKind.SecondCapture, Id.New(), Id.New(), "TestProvider", "cap_2",
            Money.Jod(18m), Money.Jod(18m), otherPaymentId: Id.New(), Now));

        var incident = PaymentIncident.Raise(
            PaymentIncidentKind.CaptureOnAnotherAttempt, Id.New(), Id.New(), "TestProvider", " cap_1 ",
            Money.Jod(18m), Money.Jod(25m), Id.New(), Now);
        Assert.Equal("cap_1", incident.CaptureReference);
        Assert.False(incident.IsHandled);
    }

    [Fact]
    public void An_incident_is_marked_handled_once_with_a_note()
    {
        var incident = PaymentIncident.Raise(
            PaymentIncidentKind.SecondCapture, Id.New(), Id.New(), "TestProvider", "cap_2",
            Money.Jod(18m), Money.Jod(18m), otherPaymentId: null, Now);
        var admin = Id.New();

        Assert.True(incident.MarkHandled(admin, "  Refunded at the provider, ticket 41.  ", Now.AddHours(1)).IsSuccess);
        Assert.True(incident.IsHandled);
        Assert.Equal(admin, incident.HandledByAdminId);
        Assert.Equal("Refunded at the provider, ticket 41.", incident.HandledNote);
        Assert.Equal(Now.AddHours(1), incident.HandledAt);

        var again = incident.MarkHandled(Id.New(), "again", Now.AddHours(2));
        Assert.Equal("payments.incident_already_handled", again.Error.Code);
        Assert.Equal(admin, incident.HandledByAdminId);
    }

    [Fact]
    public void Each_incident_outcome_maps_to_its_kind_and_the_quiet_outcomes_to_none()
    {
        Assert.Same(PaymentIncidentKind.SecondCapture, PaymentIncidentKind.For(ProviderEventOutcome.SecondCapture));
        Assert.Same(PaymentIncidentKind.AmountMismatch, PaymentIncidentKind.For(ProviderEventOutcome.AmountMismatch));
        Assert.Same(PaymentIncidentKind.CaptureOnAnotherAttempt, PaymentIncidentKind.For(ProviderEventOutcome.OtherAttempt));
        Assert.Null(PaymentIncidentKind.For(ProviderEventOutcome.Duplicate));
        Assert.Null(PaymentIncidentKind.For(ProviderEventOutcome.AssumedDuplicate));
        Assert.Null(PaymentIncidentKind.For(ProviderEventOutcome.Acted));
    }

    /// <summary>The receipt's outcome column is twenty characters; every name has to fit it.</summary>
    [Fact]
    public void Every_receipt_outcome_fits_its_column()
    {
        Assert.All(Enumeration.GetAll<ProviderEventOutcome>(), outcome => Assert.InRange(outcome.Name.Length, 1, 20));
    }
}
