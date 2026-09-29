using System.Reflection;
using Khadra.Domain.Common;
using Khadra.Domain.FinancialDocuments;
using Khadra.Domain.Payments;

namespace Khadra.Tests.Domain.FinancialDocuments;

/// <summary>
/// One receipt's email (payments Phase 7): owed only for what the customer is emailed, waiting without spending
/// anything, spending its attempt before the transport sees it, and ending once — with a history that holds no
/// address, name or body.
/// </summary>
public sealed class FinancialDocumentDeliveryTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Receipts_are_emailed_to_the_customer_and_statements_are_not()
    {
        // Owner, 2026-09-29: the one place the rule lives.
        Assert.True(FinancialDocumentType.PaymentReceipt.IsEmailedToCustomer);
        Assert.True(FinancialDocumentType.RefundReceipt.IsEmailedToCustomer);
        Assert.False(FinancialDocumentType.BookingStatement.IsEmailedToCustomer);
        Assert.Throws<DomainException>(() => FinancialDocumentDelivery.Queue(Statement(), null, Now));
    }

    [Fact]
    public void A_receipt_is_queued_due_at_once_with_nothing_spent()
    {
        var receipt = Receipt();

        var delivery = FinancialDocumentDelivery.Queue(receipt, null, Now);

        Assert.Equal(receipt.Id, delivery.DocumentId);
        Assert.Equal(FinancialDocumentDeliveryChannel.Email, delivery.Channel);
        Assert.Equal(FinancialDocumentDeliveryState.Queued, delivery.State);
        Assert.Equal(Now, delivery.QueuedAt);
        Assert.Equal(Now, delivery.NextAttemptAt);
        Assert.Equal((0, 0), (delivery.Claims, delivery.SendAttempts));
        Assert.Null(delivery.CompletedAt);
        Assert.Empty(delivery.Attempts);
        Assert.Throws<DomainException>(() => FinancialDocumentDelivery.Queue(receipt, Id.From(Guid.Empty), Now));
    }

    [Fact]
    public void Waiting_for_its_pdf_spends_nothing_writes_nothing_and_keeps_when_it_began()
    {
        var delivery = FinancialDocumentDelivery.Queue(Receipt(), null, Now);

        delivery.Wait(FinancialDocumentDeliveryWait.PdfNotReady, Now.AddMinutes(1), Now);
        delivery.Wait(FinancialDocumentDeliveryWait.PdfNotReady, Now.AddMinutes(2), Now.AddMinutes(1));

        Assert.Equal(FinancialDocumentDeliveryState.Queued, delivery.State);
        Assert.Equal(FinancialDocumentDeliveryWait.PdfNotReady, delivery.WaitingReason);
        Assert.Equal(Now, delivery.WaitingSince);
        Assert.Equal(Now.AddMinutes(2), delivery.NextAttemptAt);
        Assert.Equal(0, delivery.SendAttempts);
        Assert.Empty(delivery.Attempts);
    }

    [Fact]
    public void A_send_attempt_is_spent_before_the_transport_is_called_and_holds_the_email()
    {
        var delivery = FinancialDocumentDelivery.Queue(Receipt(), null, Now);
        delivery.Wait(FinancialDocumentDeliveryWait.PdfNotReady, Now.AddMinutes(1), Now);

        delivery.BeginSend("rana@example.jo", "ar,en", Now.AddMinutes(5));

        Assert.Equal(1, delivery.SendAttempts);
        Assert.Equal("rana@example.jo", delivery.RecipientAddress);
        Assert.Equal("ar,en", delivery.Languages);
        Assert.Null(delivery.WaitingReason);
        Assert.Null(delivery.WaitingSince);
        Assert.Equal(Now.AddMinutes(5), delivery.NextAttemptAt);
        // Nothing is recorded until the transport has answered.
        Assert.Empty(delivery.Attempts);
    }

    [Fact]
    public void Accepted_is_Sent_once_and_nothing_after_it_changes_that()
    {
        var delivery = FinancialDocumentDelivery.Queue(Receipt(), null, Now);
        delivery.BeginSend("rana@example.jo", "en", Now.AddMinutes(5));
        var (english, arabic) = (Id.New(), Id.New());

        delivery.RecordAccepted("Brevo", "<abc@smtp-relay.mailin.fr>", english, arabic, Now.AddSeconds(2));
        delivery.RecordFailed("late refusal", "Brevo", null, null, 5, Now.AddHours(1), Now.AddSeconds(3));
        delivery.RecordSkipped("late skip", Now.AddSeconds(4));

        Assert.Equal(FinancialDocumentDeliveryState.Sent, delivery.State);
        Assert.Equal(Now.AddSeconds(2), delivery.CompletedAt);
        var attempt = Assert.Single(delivery.Attempts);
        Assert.Equal((1, FinancialDocumentDeliveryOutcome.Accepted), (attempt.Number, attempt.Outcome));
        Assert.Equal(("Brevo", "<abc@smtp-relay.mailin.fr>"), (attempt.Provider, attempt.ProviderMessageId));
        Assert.Equal((english, arabic), (attempt.EnglishRenditionId!.Value, attempt.ArabicRenditionId!.Value));
        Assert.Throws<DomainException>(() => delivery.BeginSend("rana@example.jo", "en", Now.AddMinutes(9)));
        Assert.Throws<DomainException>(() => delivery.Wait(FinancialDocumentDeliveryWait.PdfNotReady, Now.AddMinutes(9), Now));
    }

    [Fact]
    public void A_refusal_is_retried_until_the_last_send_attempt_and_then_the_email_has_Failed()
    {
        var delivery = FinancialDocumentDelivery.Queue(Receipt(), null, Now);

        for (var send = 1; send <= 3; send++)
        {
            delivery.BeginSend("rana@example.jo", "en", Now.AddMinutes(send));
            delivery.RecordFailed($"refused {send}", "Smtp", null, null, maxSendAttempts: 3, Now.AddHours(send), Now.AddMinutes(send));
            if (send < 3)
            {
                Assert.Equal(FinancialDocumentDeliveryState.Queued, delivery.State);
                Assert.Equal(Now.AddHours(send), delivery.NextAttemptAt);
            }
        }

        Assert.Equal(FinancialDocumentDeliveryState.Failed, delivery.State);
        Assert.Equal(Now.AddMinutes(3), delivery.CompletedAt);
        Assert.Equal("refused 3", delivery.LastError);
        Assert.Equal([1, 2, 3], delivery.Attempts.Select(attempt => attempt.Number));
        Assert.All(delivery.Attempts, attempt => Assert.Equal(FinancialDocumentDeliveryOutcome.Failed, attempt.Outcome));
    }

    [Fact]
    public void An_email_that_waited_and_then_ended_no_longer_waits_for_anything()
    {
        // A receipt voided while its email waited for the PDF is Skipped; a PDF found altered once drawn is given up.
        var skipped = FinancialDocumentDelivery.Queue(Receipt(), null, Now);
        skipped.Wait(FinancialDocumentDeliveryWait.PdfNotReady, Now.AddMinutes(1), Now);
        skipped.RecordSkipped("Voided before it was emailed; its correction is emailed instead.", Now.AddMinutes(1));

        var givenUp = FinancialDocumentDelivery.Queue(Receipt(), null, Now);
        givenUp.Wait(FinancialDocumentDeliveryWait.PdfNotReady, Now.AddMinutes(1), Now);
        givenUp.GiveUp("rendition_altered", Now.AddMinutes(1));

        foreach (var ended in new[] { skipped, givenUp })
        {
            Assert.True(ended.State.IsFinal);
            Assert.Null(ended.WaitingReason);
            Assert.Null(ended.WaitingSince);
        }
    }

    [Fact]
    public void Skipped_and_given_up_end_it_without_spending_a_send_attempt()
    {
        var skipped = FinancialDocumentDelivery.Queue(Receipt(), null, Now);
        skipped.RecordSkipped("The customer has no verified email address.", Now);
        Assert.Equal(FinancialDocumentDeliveryState.Skipped, skipped.State);
        Assert.Equal(0, skipped.SendAttempts);
        Assert.Equal(FinancialDocumentDeliveryOutcome.Skipped, Assert.Single(skipped.Attempts).Outcome);

        var givenUp = FinancialDocumentDelivery.Queue(Receipt(), null, Now);
        givenUp.GiveUp("rendition_altered", Now);
        Assert.Equal(FinancialDocumentDeliveryState.Failed, givenUp.State);
        Assert.Equal(0, givenUp.SendAttempts);
        Assert.Equal("rendition_altered", Assert.Single(givenUp.Attempts).Error);
    }

    [Fact]
    public void The_history_is_append_only_and_holds_no_address_name_or_body()
    {
        Assert.True(typeof(IAppendOnly).IsAssignableFrom(typeof(FinancialDocumentDeliveryAttempt)));
        Assert.False(typeof(IAppendOnly).IsAssignableFrom(typeof(FinancialDocumentDelivery)));
        // Nothing on an attempt could carry personal data: only what happened, when, who said so, and which PDFs.
        var properties = typeof(FinancialDocumentDeliveryAttempt)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal);
        Assert.Equal(
            ["ArabicRenditionId", "AttemptedAt", "DeliveryId", "EnglishRenditionId", "Error", "Id", "Number", "Outcome", "Provider", "ProviderMessageId"],
            properties);

        var delivery = FinancialDocumentDelivery.Queue(Receipt(), null, Now);
        delivery.BeginSend("rana@example.jo", "en", Now.AddMinutes(5));
        delivery.RecordAccepted("Smtp", new string('x', 250), null, null, Now);
        Assert.Equal(FinancialDocumentDeliveryAttempt.MaxProviderMessageIdLength, Assert.Single(delivery.Attempts).ProviderMessageId!.Length);
        Assert.DoesNotContain("rana@example.jo", string.Join('|', delivery.Attempts.Select(attempt => $"{attempt.Error}{attempt.Provider}{attempt.ProviderMessageId}")), StringComparison.Ordinal);
    }

    private static FinancialDocument Receipt() => Issue(FinancialDocumentType.PaymentReceipt);

    private static FinancialDocument Statement() => Issue(FinancialDocumentType.BookingStatement);

    private static FinancialDocument Issue(FinancialDocumentType type)
    {
        var bookingId = Id.New();
        var paymentId = Id.New();
        var statement = type == FinancialDocumentType.BookingStatement;
        var draft = new FinancialDocumentDraft(
            type,
            statement ? bookingId : paymentId,
            Version: 1,
            PreviousVersionId: null,
            RelatedDocumentId: null,
            BookingId: bookingId,
            BookingReference: "KH-EMAIL001",
            CustomerId: Id.New(),
            DealerId: Id.New(),
            PaymentId: statement ? null : paymentId,
            RefundId: null,
            statement ? FinancialDocumentCause.PaymentCaptured : FinancialDocumentCause.PaymentCaptured,
            OccurredAt: Now,
            CoversThrough: statement ? Now : null,
            CheckpointFingerprint: statement ? new string('c', 64) : null,
            HeadlineAmount: Money.Jod(18m),
            Provider: PaymentProviders.Sandbox,
            CalculatorVersion: 1,
            SnapshotSchemaVersion: 1,
            Snapshot: "{\"schemaVersion\":1}");
        return FinancialDocument.Issue(draft, statement ? "TEST-STM-2026-000001" : "TEST-PAY-2026-000001", Now);
    }
}
