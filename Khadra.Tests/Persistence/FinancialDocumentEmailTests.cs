using System.Security.Cryptography;
using Khadra.Application.FinancialDocuments.Email;
using Khadra.Application.FinancialDocuments.Queries;
using Khadra.Application.FinancialDocuments.ReadModels;
using Khadra.Domain.Auditing;
using Khadra.Domain.Common;
using Khadra.Domain.FinancialDocuments;
using Khadra.Domain.FinancialDocuments.Repositories;
using Khadra.Domain.Payments;
using Khadra.Domain.IdentityAccess;
using Khadra.Infrastructure.Persistence;
using Khadra.Infrastructure.Persistence.Repositories;
using Khadra.Infrastructure.Reporting;
using Khadra.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Tests.Persistence;

/// <summary>
/// Emailing issued receipts with their PDFs, end to end on SQLite (payments Phase 7; owner, 2026-09-29): owed in the
/// transaction that issues a receipt and never for a statement; waiting for the PDF rather than going without it;
/// sent in the customer's language at the moment of sending, or both; not sent at all to an unverified address, for a
/// receipt voided first, or from a host whose transport delivers nothing; retried, then given up; worked by one
/// process only, whatever the leases do, and retried after a crash as the same message; and asked for again by an
/// administrator, audited — with the history the administrator reads.
/// </summary>
public sealed class FinancialDocumentEmailTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly IssuanceHarness _harness;

    public FinancialDocumentEmailTests()
    {
        _connection.Open();
        var options = new DbContextOptionsBuilder<KhadraDbContext>()
            .UseSqlite(_connection)
            .UseSnakeCaseNamingConvention()
            .Options;
        using var context = new KhadraDbContext(options);
        context.Database.EnsureCreated();
        _harness = new IssuanceHarness(options);
    }

    public void Dispose() => _connection.Dispose();

    // ── Owed at issue ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_receipt_is_owed_its_email_when_it_is_issued_and_a_statement_is_owed_none()
    {
        var (receipt, statement) = await IssuedAsync();

        var delivery = Assert.Single(await _harness.DeliveriesAsync());
        Assert.Equal(receipt.Id, delivery.DocumentId);
        Assert.DoesNotContain(await _harness.DeliveriesAsync(), candidate => candidate.DocumentId == statement.Id);
        Assert.Equal(FinancialDocumentDeliveryState.Queued, delivery.State);
        Assert.Null(delivery.RequestedByAdminId);
        Assert.Equal(receipt.IssuedAt, delivery.QueuedAt);
        Assert.Empty(delivery.Attempts);
    }

    // ── Never without its PDF ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Before_its_pdf_is_drawn_the_email_waits_spending_nothing_and_writing_no_attempt()
    {
        await IssuedAsync();

        var outcome = Assert.Single(await _harness.EmailPassAsync());

        Assert.True(outcome.Waiting);
        Assert.Empty(_harness.Mail.Messages);
        var delivery = Assert.Single(await _harness.DeliveriesAsync());
        Assert.Equal(FinancialDocumentDeliveryState.Queued, delivery.State);
        Assert.Equal(FinancialDocumentDeliveryWait.PdfNotReady, delivery.WaitingReason);
        Assert.Equal(_harness.Now, delivery.WaitingSince);
        Assert.Equal(_harness.Now.Add(_harness.EmailSettings.PdfWaitDelay), delivery.NextAttemptAt);
        Assert.Equal(0, delivery.SendAttempts);
        Assert.Equal(1, delivery.Claims);
        Assert.Empty(delivery.Attempts);

        // Not due again until its wait is over; and waiting again keeps when it started waiting.
        Assert.Empty(await _harness.EmailPassAsync());
        _harness.Now = _harness.Now.AddMinutes(1);
        Assert.True(Assert.Single(await _harness.EmailPassAsync()).Waiting);
        Assert.Equal(IssuanceHarness.Start.AddMinutes(1), Assert.Single(await _harness.DeliveriesAsync()).WaitingSince);
    }

    [Fact]
    public async Task Once_drawn_it_is_sent_with_both_pdfs_to_a_customer_who_never_chose_a_language()
    {
        var (receipt, _) = await IssuedAsync();
        await _harness.EmailPassAsync();
        await _harness.RenderPassAsync();
        _harness.Now = _harness.Now.AddMinutes(1);

        var outcome = Assert.Single(await _harness.EmailPassAsync());

        Assert.Equal("Sent", outcome.State);
        var message = Assert.Single(_harness.Mail.Messages);
        Assert.Equal("rana@example.jo", message.ToAddress);
        Assert.Equal("Rana Sharif", message.ToName);
        Assert.Equal($"إيصال دفع · Payment receipt {receipt.Number}", message.Subject);
        Assert.StartsWith("مرحباً Rana Sharif،", message.TextBody, StringComparison.Ordinal);
        Assert.Contains("Hi Rana Sharif,", message.TextBody, StringComparison.Ordinal);

        // Each PDF exactly as stored, named as the website saves it — in the email's own order, Arabic first as its text is.
        var renditions = (await _harness.RenditionsAsync()).Where(rendition => rendition.DocumentId == receipt.Id).ToList();
        Assert.Equal([$"{receipt.Number}-ar.pdf", $"{receipt.Number}-en.pdf"], message.Attachments.Select(file => file.FileName));
        string[] order = ["ar", "en"];
        foreach (var (file, language) in message.Attachments.Zip(order))
        {
            var stored = renditions.Single(rendition => rendition.Language.Name == language && rendition.Kind == RenditionKind.AsIssued);
            Assert.Equal("application/pdf", file.ContentType);
            Assert.Equal(stored.ContentSha256, Convert.ToHexStringLower(SHA256.HashData(file.Content)));
        }

        Assert.StartsWith("fd-", message.IdempotencyKey, StringComparison.Ordinal);
        // Replies go to Khadra's support address, never to the no-reply sender (owner, 2026-09-29).
        Assert.Equal(DocumentFixtures.Issuer.SupportEmail, message.ReplyTo);
        // Both PDFs, and each language says two are attached.
        Assert.Contains("two PDFs, in Arabic and English", message.TextBody, StringComparison.Ordinal);
        Assert.Contains("ملفَّي PDF", message.TextBody, StringComparison.Ordinal);

        var delivery = Assert.Single(await _harness.DeliveriesAsync());
        Assert.Equal(FinancialDocumentDeliveryState.Sent, delivery.State);
        Assert.Equal(_harness.Now, delivery.CompletedAt);
        Assert.Equal("rana@example.jo", delivery.RecipientAddress);
        Assert.Equal("ar,en", delivery.Languages);
        Assert.Null(delivery.WaitingReason);
        Assert.Equal(1, delivery.SendAttempts);
        var attempt = Assert.Single(delivery.Attempts);
        Assert.Equal(FinancialDocumentDeliveryOutcome.Accepted, attempt.Outcome);
        Assert.Equal("Smtp", attempt.Provider);
        Assert.Equal("<test-1@khadra.test>", attempt.ProviderMessageId);
        Assert.Equal(renditions.Single(rendition => rendition.Language == Language.English && rendition.Kind == RenditionKind.AsIssued).Id, attempt.EnglishRenditionId);
        Assert.Equal(renditions.Single(rendition => rendition.Language == Language.Arabic && rendition.Kind == RenditionKind.AsIssued).Id, attempt.ArabicRenditionId);

        // Sent once: later passes find nothing to do.
        Assert.Empty(await _harness.EmailPassAsync());
        Assert.Single(_harness.Mail.Messages);
    }

    [Theory]
    [InlineData("ar", "إيصال دفع", "مرحباً Rana Sharif،")]
    [InlineData("en", "Payment receipt", "Hi Rana Sharif,")]
    public async Task A_customer_who_chose_a_language_is_written_in_it_with_that_pdf_alone(string language, string title, string greeting)
    {
        _harness.CustomerLanguage = Enumeration.FromName<Language>(language);
        var (receipt, _) = await DrawnAsync();

        await _harness.EmailPassAsync();

        var message = Assert.Single(_harness.Mail.Messages);
        Assert.Equal($"{title} {receipt.Number}", message.Subject);
        Assert.StartsWith(greeting, message.TextBody, StringComparison.Ordinal);
        Assert.Equal([$"{receipt.Number}-{language}.pdf"], message.Attachments.Select(file => file.FileName));
        Assert.Equal(language, Assert.Single(await _harness.DeliveriesAsync()).Languages);
    }

    [Fact]
    public async Task With_one_of_its_two_pdfs_drawn_the_email_still_waits_for_the_other()
    {
        var (receipt, _) = await IssuedAsync();
        _harness.Undrawable.Add(new RenditionCandidate(receipt.Id, Language.English, RenditionKind.AsIssued));
        await _harness.RenderPassAsync();
        var drawn = (await _harness.RenditionsAsync()).Where(rendition => rendition.DocumentId == receipt.Id).ToList();
        Assert.Equal(Language.Arabic, Assert.Single(drawn).Language);

        var outcome = Assert.Single(await _harness.EmailPassAsync());

        // The Arabic PDF was read, and still nothing went: a customer who never chose is owed both.
        Assert.True(outcome.Waiting);
        Assert.Empty(_harness.Mail.Messages);
        var delivery = Assert.Single(await _harness.DeliveriesAsync());
        Assert.Equal(FinancialDocumentDeliveryWait.PdfNotReady, delivery.WaitingReason);
        Assert.Equal(0, delivery.SendAttempts);
        Assert.Empty(delivery.Attempts);
    }

    [Fact]
    public async Task An_email_that_waited_for_its_pdf_and_then_ended_no_longer_says_it_is_waiting()
    {
        var (receipt, _) = await IssuedAsync();
        Assert.True(Assert.Single(await _harness.EmailPassAsync()).Waiting);
        _harness.Now = _harness.Now.AddMinutes(1);
        Assert.True((await _harness.VoidAsync(receipt.Id, "Issued from the wrong capture.")).IsSuccess);

        await _harness.EmailPassAsync();

        // Skipped, with no wait above its end — the database's check would refuse the row otherwise.
        var original = (await _harness.DeliveriesAsync()).Single(delivery => delivery.DocumentId == receipt.Id);
        Assert.Equal(FinancialDocumentDeliveryState.Skipped, original.State);
        Assert.Null(original.WaitingReason);
        Assert.Null(original.WaitingSince);
        Assert.Null(Assert.Single((await PageAsync(receipt.Id)).Emails).WaitingFor);
    }

    [Fact]
    public async Task A_stored_pdf_that_no_longer_matches_its_record_is_never_sent_and_the_email_is_given_up()
    {
        var (receipt, _) = await DrawnAsync();
        var english = (await _harness.RenditionsAsync()).Single(rendition =>
            rendition.DocumentId == receipt.Id && rendition.Language == Language.English && rendition.Kind == RenditionKind.AsIssued);
        _harness.Storage.Replace(english.StorageKey, [0x25, 0x50, 0x44, 0x46, 0x2D, 0x39]);

        var outcome = Assert.Single(await _harness.EmailPassAsync());

        Assert.Equal("Failed", outcome.State);
        Assert.Empty(_harness.Mail.Messages);
        var delivery = Assert.Single(await _harness.DeliveriesAsync());
        Assert.Equal(0, delivery.SendAttempts);
        Assert.Contains("rendition_altered", delivery.LastError, StringComparison.Ordinal);
        Assert.Equal(FinancialDocumentDeliveryOutcome.Failed, Assert.Single(delivery.Attempts).Outcome);
        Assert.True(_harness.EmailLog.Logged(2702));
    }

    // ── Not sent, on purpose ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_customer_with_no_verified_address_is_not_written_to()
    {
        _harness.CustomerEmailVerified = false;
        await DrawnAsync();

        var outcome = Assert.Single(await _harness.EmailPassAsync());

        Assert.Equal("Skipped", outcome.State);
        Assert.Empty(_harness.Mail.Messages);
        var delivery = Assert.Single(await _harness.DeliveriesAsync());
        Assert.Equal("The customer has no verified email address.", delivery.LastError);
        Assert.Null(delivery.RecipientAddress);
        Assert.Equal(FinancialDocumentDeliveryOutcome.Skipped, Assert.Single(delivery.Attempts).Outcome);
    }

    [Fact]
    public async Task An_administrators_resend_is_held_to_the_same_rule_checked_as_it_is_sent()
    {
        _harness.CustomerEmailVerified = false;
        var (receipt, _) = await DrawnAsync();
        await _harness.EmailPassAsync();

        // Asked for again, it is queued — and still checked against the customer's CURRENT address as it is sent.
        Assert.True((await _harness.RequestEmailAsync(receipt.Id)).IsSuccess);
        Assert.Equal("Skipped", Assert.Single(await _harness.EmailPassAsync()).State);

        Assert.Empty(_harness.Mail.Messages);
        Assert.All(await _harness.DeliveriesAsync(), delivery =>
        {
            Assert.Equal(FinancialDocumentDeliveryState.Skipped, delivery.State);
            Assert.Equal("The customer has no verified email address.", delivery.LastError);
        });
    }

    [Fact]
    public async Task A_test_receipt_goes_through_a_real_provider_only_to_an_address_on_the_allowlist()
    {
        // A real provider, not the local Mailpit (owner, 2026-09-29).
        _harness.EmailSettings.TransportCapturesMail = false;
        _harness.EmailSettings.TransportName = "Brevo";
        var (receipt, _) = await DrawnAsync();
        Assert.True(receipt.IsTest);

        Assert.Equal("Skipped", Assert.Single(await _harness.EmailPassAsync()).State);

        Assert.Empty(_harness.Mail.Messages);
        var skipped = Assert.Single(await _harness.DeliveriesAsync());
        Assert.Equal(
            "A TEST document goes through a real mail provider only to an address on FinancialDocuments:Email:TestRecipients.",
            skipped.LastError);
        Assert.DoesNotContain("@", skipped.LastError, StringComparison.Ordinal);

        // On the allowlist — in any case — the same receipt goes.
        _harness.EmailSettings.TestRecipients.Add("RANA@example.jo");
        Assert.True((await _harness.RequestEmailAsync(receipt.Id)).IsSuccess);
        Assert.Equal("Sent", Assert.Single(await _harness.EmailPassAsync()).State);
        Assert.Equal("rana@example.jo", Assert.Single(_harness.Mail.Messages).ToAddress);
    }

    [Fact]
    public async Task With_no_support_address_configured_a_receipt_sets_no_reply_to()
    {
        _harness.EmailSettings.ReplyTo = null;
        await DrawnAsync();

        await _harness.EmailPassAsync();

        Assert.Null(Assert.Single(_harness.Mail.Messages).ReplyTo);
    }

    [Fact]
    public async Task A_host_whose_transport_delivers_nothing_says_so_rather_than_Sent()
    {
        _harness.EmailSettings.TransportDeliversMail = false;
        _harness.EmailSettings.TransportName = "Logging";
        await DrawnAsync();

        Assert.Equal("Skipped", Assert.Single(await _harness.EmailPassAsync()).State);

        Assert.Empty(_harness.Mail.Messages);
        Assert.Contains("Logging", Assert.Single(await _harness.DeliveriesAsync()).LastError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_receipt_voided_before_its_email_went_is_not_sent_and_its_correction_is_with_its_own_pdfs()
    {
        var (receipt, _) = await IssuedAsync();
        _harness.Now = _harness.Now.AddMinutes(1);
        var voided = await _harness.VoidAsync(receipt.Id, "Issued from the wrong capture.");
        Assert.True(voided.IsSuccess);
        await _harness.RenderPassAsync();

        var outcomes = await _harness.EmailPassAsync();

        Assert.Equal(["Sent", "Skipped"], outcomes.Select(outcome => outcome.State).Order(StringComparer.Ordinal));
        var deliveries = await _harness.DeliveriesAsync();
        var original = deliveries.Single(delivery => delivery.DocumentId == receipt.Id);
        Assert.Equal(FinancialDocumentDeliveryState.Skipped, original.State);
        Assert.Contains("its correction is emailed instead", original.LastError, StringComparison.Ordinal);

        // The correction says what it corrects, and carries its own PDFs alone — never the voided receipt's, and never its
        // voided copy, which stays in the customer's account (owner, 2026-09-29).
        var message = Assert.Single(_harness.Mail.Messages);
        Assert.Contains(voided.Value.ReplacementNumber, message.Subject, StringComparison.Ordinal);
        Assert.Contains($"This receipt corrects {receipt.Number}, which was voided.", message.TextBody, StringComparison.Ordinal);
        Assert.Equal(
            [$"{voided.Value.ReplacementNumber}-ar.pdf", $"{voided.Value.ReplacementNumber}-en.pdf"],
            message.Attachments.Select(file => file.FileName));
        var correction = (await _harness.RenditionsAsync())
            .Where(rendition => rendition.DocumentId.Value == voided.Value.ReplacementDocumentId && rendition.Kind == RenditionKind.AsIssued)
            .Select(rendition => rendition.ContentSha256)
            .Order(StringComparer.Ordinal);
        Assert.Equal(correction, message.Attachments.Select(file => Convert.ToHexStringLower(SHA256.HashData(file.Content))).Order(StringComparer.Ordinal));
    }

    // ── Retried, then given up ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_refusing_transport_is_retried_and_then_given_up_with_its_reason_kept_and_no_address_in_it()
    {
        await DrawnAsync();
        _harness.Mail.Refusal = new InvalidOperationException("Brevo refused the message (400 Bad Request): unknown recipient rana@example.jo");

        var first = Assert.Single(await _harness.EmailPassAsync());

        Assert.Equal("Queued", first.State);
        var delivery = Assert.Single(await _harness.DeliveriesAsync());
        Assert.Equal(1, delivery.SendAttempts);
        Assert.Equal(_harness.Now.Add(_harness.EmailSettings.RetryDelay(1)), delivery.NextAttemptAt);
        Assert.Equal("Brevo refused the message (400 Bad Request): unknown recipient (an address)", delivery.LastError);
        Assert.Equal("Smtp", Assert.Single(delivery.Attempts).Provider);

        for (var send = 2; send <= _harness.EmailSettings.MaxSendAttempts; send++)
        {
            _harness.Now = _harness.Now.AddHours(1);
            await _harness.EmailPassAsync();
        }

        delivery = Assert.Single(await _harness.DeliveriesAsync());
        Assert.Equal(FinancialDocumentDeliveryState.Failed, delivery.State);
        Assert.Equal(_harness.EmailSettings.MaxSendAttempts, delivery.SendAttempts);
        Assert.Equal(_harness.EmailSettings.MaxSendAttempts, delivery.Attempts.Count);
        Assert.All(delivery.Attempts, attempt => Assert.DoesNotContain("@", attempt.Error, StringComparison.Ordinal));
        Assert.Empty(await _harness.EmailPassAsync());
    }

    [Fact]
    public async Task A_failure_from_elsewhere_is_recorded_by_its_kind_alone()
    {
        await DrawnAsync();
        _harness.Mail.Refusal = new IOException("socket to smtp.example.jo for rana@example.jo reset");

        await _harness.EmailPassAsync();

        Assert.Equal("IOException", Assert.Single(await _harness.DeliveriesAsync()).LastError);
    }

    [Fact]
    public async Task A_retry_is_written_in_the_languages_of_the_first_send_under_the_same_key_and_a_new_email_asks_again()
    {
        _harness.CustomerLanguage = Language.English;
        var (receipt, _) = await DrawnAsync();
        var keys = new List<string?>();
        _harness.Mail.OnSend = message =>
        {
            keys.Add(message.IdempotencyKey);
            return Task.CompletedTask;
        };
        _harness.Mail.Refusal = new InvalidOperationException("Resend refused the message (503 Service Unavailable): try again");
        await _harness.EmailPassAsync();

        // The customer switches to Arabic before the retry.
        await _harness.ChangeAsync(async context =>
            (await context.Users.SingleAsync(user => user.Id == receipt.CustomerId)).ChoosePreferredLanguage(Language.Arabic));
        _harness.Mail.Refusal = null;
        _harness.Now = _harness.Now.AddHours(1);
        await _harness.EmailPassAsync();

        // The same message as the one that may already have gone: English, under the same key.
        var retry = Assert.Single(_harness.Mail.Messages);
        Assert.Equal($"Payment receipt {receipt.Number}", retry.Subject);
        Assert.Equal([$"{receipt.Number}-en.pdf"], retry.Attachments.Select(file => file.FileName));
        Assert.Equal(2, keys.Count);
        Assert.Equal(keys[0], keys[1]);

        // A new email is a new delivery, and reads the choice again.
        Assert.True((await _harness.RequestEmailAsync(receipt.Id)).IsSuccess);
        await _harness.EmailPassAsync();
        Assert.Equal($"إيصال دفع {receipt.Number}", _harness.Mail.Messages[1].Subject);
        Assert.NotEqual(keys[0], keys[2]);
    }

    // ── Switched off: Production on Brevo (owner, 2026-09-29; pre-launch item 202) ─────────────────

    [Fact]
    public async Task On_a_server_whose_delivery_is_switched_off_nothing_is_sent_and_every_email_waits_owed()
    {
        _harness.EmailSettings.DeliveryDisabledReason = "Financial-document email delivery is disabled on this server.";
        var (receipt, _) = await DrawnAsync();

        // The email service does not run there; and should anything reach the last step, it sends nothing and writes
        // nothing — the email owed at issue stays queued.
        var claimed = Assert.Single(await _harness.ClaimEmailsAsync());
        Assert.Same(FinancialDocumentEmailOutcome.Untouched, await _harness.EmailAsync(claimed));
        Assert.Empty(_harness.Mail.Messages);
        var waiting = Assert.Single(await _harness.DeliveriesAsync());
        Assert.Equal((FinancialDocumentDeliveryState.Queued, 0), (waiting.State, waiting.SendAttempts));
        Assert.Empty(waiting.Attempts);

        // An administrator's request is refused — nothing queued, nothing audited — and the refusal is logged.
        Assert.Equal(FinancialDocumentErrors.EmailDeliveryDisabled, (await _harness.RequestEmailAsync(receipt.Id)).Error);
        Assert.Single(await _harness.DeliveriesAsync());
        await using (var context = _harness.NewContext())
            Assert.False(await context.AuditEntries.AnyAsync(entry => entry.Action == AuditAction.FinancialDocumentEmailRequested));
        Assert.True(_harness.RequestLog.Logged(2705));
        Assert.DoesNotContain("@", _harness.RequestLog.AllText, StringComparison.Ordinal);

        // The administrator's page says so, and offers no way to send.
        var page = await PageAsync(receipt.Id);
        Assert.True(page.EmailDeliveryDisabled);
        Assert.False(page.CanEmailAgain);

        // Once the server may send again, the email that waited goes: owed, never lost.
        _harness.EmailSettings.DeliveryDisabledReason = null;
        _harness.Now = _harness.Now.Add(_harness.EmailSettings.Lease);
        Assert.Equal("Sent", Assert.Single(await _harness.EmailPassAsync()).State);
        Assert.False((await PageAsync(receipt.Id)).EmailDeliveryDisabled);
    }

    // ── One process per email ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_process_that_stops_mid_send_has_spent_the_attempt_and_its_retry_is_the_same_message_under_the_same_key()
    {
        await DrawnAsync();
        using var stopping = new CancellationTokenSource();
        string? interrupted = null;
        _harness.Mail.OnSend = message =>
        {
            // The process stops while the transport has the message: whether it went is unknown.
            interrupted = message.IdempotencyKey;
            stopping.Cancel();
            stopping.Token.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _harness.EmailPassAsync(stopping.Token));

        var held = Assert.Single(await _harness.DeliveriesAsync());
        Assert.Equal(FinancialDocumentDeliveryState.Queued, held.State);
        Assert.Equal(1, held.SendAttempts);
        Assert.Empty(held.Attempts);
        Assert.Equal(_harness.Now.Add(_harness.EmailSettings.Lease), held.NextAttemptAt);
        Assert.Empty(_harness.Mail.Messages);

        // Held until the lease runs out; then sent again — the same message, under the same key, so a provider that
        // honours the key drops it if the first one went.
        _harness.Mail.OnSend = null;
        Assert.Empty(await _harness.EmailPassAsync());
        _harness.Now = _harness.Now.Add(_harness.EmailSettings.Lease);
        Assert.Equal("Sent", Assert.Single(await _harness.EmailPassAsync()).State);
        Assert.Equal(interrupted, Assert.Single(_harness.Mail.Messages).IdempotencyKey);
        Assert.Equal(2, Assert.Single(await _harness.DeliveriesAsync()).SendAttempts);
    }

    [Fact]
    public async Task A_process_whose_claim_ran_out_and_was_taken_over_leaves_the_email_to_the_process_that_took_it()
    {
        await DrawnAsync();

        // This process claims the email and stalls past its lease; another claims it again.
        var stalled = Assert.Single(await _harness.ClaimEmailsAsync());
        _harness.Now = _harness.Now.Add(_harness.EmailSettings.Lease).AddSeconds(1);
        var fresh = Assert.Single(await _harness.ClaimEmailsAsync());
        Assert.Equal(stalled.DeliveryId, fresh.DeliveryId);
        Assert.Equal(stalled.Claims + 1, fresh.Claims);

        // The stalled one reaches the email while the other is sending it: it neither sends nor writes.
        FinancialDocumentEmailOutcome? meanwhile = null;
        _harness.Mail.OnSend = async _ => meanwhile = await _harness.EmailAsync(stalled);
        Assert.Equal("Sent", (await _harness.EmailAsync(fresh)).State);

        Assert.Same(FinancialDocumentEmailOutcome.Untouched, meanwhile);
        // …nor once it has gone.
        Assert.Same(FinancialDocumentEmailOutcome.Untouched, await _harness.EmailAsync(stalled));
        Assert.Single(_harness.Mail.Messages);
        var delivery = Assert.Single(await _harness.DeliveriesAsync());
        Assert.Equal(1, delivery.SendAttempts);
        Assert.Single(delivery.Attempts);
    }

    [Fact]
    public async Task A_claim_taken_over_while_the_pdfs_are_read_spends_nothing_and_writes_nothing()
    {
        await DrawnAsync();
        var mine = Assert.Single(await _harness.ClaimEmailsAsync());

        // While this process reads the PDFs, its lease runs out and another process claims the email.
        ClaimedFinancialDocumentDelivery? theirs = null;
        _harness.Storage.BeforeOpen = async _ =>
        {
            _harness.Storage.BeforeOpen = null;
            _harness.Now = _harness.Now.Add(_harness.EmailSettings.Lease).AddSeconds(1);
            theirs = Assert.Single(await _harness.ClaimEmailsAsync());
        };

        // Its attempt would be saved over a claim that is no longer its own: the database refuses, nothing lands.
        Assert.Same(FinancialDocumentEmailOutcome.Untouched, await _harness.EmailAsync(mine));
        Assert.Empty(_harness.Mail.Messages);
        var delivery = Assert.Single(await _harness.DeliveriesAsync());
        Assert.Equal((0, 2), (delivery.SendAttempts, delivery.Claims));

        Assert.Equal("Sent", (await _harness.EmailAsync(theirs!)).State);
        Assert.Single(_harness.Mail.Messages);
    }

    // ── Asked for again ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_administrator_can_email_a_receipt_again_once_its_email_ended_audited_by_its_number()
    {
        var (receipt, statement) = await DrawnAsync();

        // Refused while its first email is still on its way…
        Assert.Equal(FinancialDocumentErrors.EmailAlreadyQueued, (await _harness.RequestEmailAsync(receipt.Id)).Error);
        await _harness.EmailPassAsync();

        // …and for what is not emailed at all, or not there.
        Assert.Equal(FinancialDocumentErrors.NotEmailed, (await _harness.RequestEmailAsync(statement.Id)).Error);
        Assert.Equal(FinancialDocumentErrors.NotFound, (await _harness.RequestEmailAsync(Id.New())).Error);

        _harness.Now = _harness.Now.AddMinutes(5);
        var admin = Id.New();
        var requested = await _harness.RequestEmailAsync(receipt.Id, admin);
        Assert.True(requested.IsSuccess);
        Assert.Equal(FinancialDocumentErrors.EmailAlreadyQueued, (await _harness.RequestEmailAsync(receipt.Id)).Error);

        await _harness.EmailPassAsync();

        Assert.Equal(2, _harness.Mail.Messages.Count);
        var again = (await _harness.DeliveriesAsync()).Single(delivery => delivery.Id.Value == requested.Value.DeliveryId);
        Assert.Equal(admin, again.RequestedByAdminId);
        Assert.Equal(FinancialDocumentDeliveryState.Sent, again.State);
        // A new email is a new message: its own key.
        Assert.NotEqual(_harness.Mail.Messages[0].IdempotencyKey, _harness.Mail.Messages[1].IdempotencyKey);

        await using var context = _harness.NewContext();
        var entry = await context.AuditEntries.AsNoTracking().SingleAsync(candidate => candidate.Action == AuditAction.FinancialDocumentEmailRequested);
        Assert.Equal(receipt.Id, entry.EntityId);
        Assert.Equal(receipt.Number, entry.SubjectLabel);
        Assert.DoesNotContain("@", $"{entry.SubjectLabel}{entry.PreviousValue}{entry.NewValue}{entry.Reason}", StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_voided_receipt_is_not_emailed_again_its_correction_is()
    {
        var (receipt, _) = await DrawnAsync();
        await _harness.EmailPassAsync();
        var voided = await _harness.VoidAsync(receipt.Id, "Duplicate capture.");

        Assert.Equal(FinancialDocumentErrors.VoidedNotEmailed, (await _harness.RequestEmailAsync(receipt.Id)).Error);
        // The correction's own email was owed with it; once that has gone, it may be asked for again.
        await _harness.RenderPassAsync();
        await _harness.EmailPassAsync();
        Assert.True((await _harness.RequestEmailAsync(Id.From(voided.Value.ReplacementDocumentId))).IsSuccess);
    }

    [Fact]
    public async Task The_database_holds_one_queued_email_per_document_and_the_history_is_append_only()
    {
        var (receipt, _) = await DrawnAsync();
        var document = (await _harness.DocumentsAsync()).Single(candidate => candidate.Id == receipt.Id);

        await using (var second = _harness.NewContext())
        {
            second.FinancialDocumentDeliveries.Add(FinancialDocumentDelivery.Queue(document, Id.New(), _harness.Now));
            await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
        }

        await _harness.EmailPassAsync();
        await using var context = _harness.NewContext();
        var attempt = await context.FinancialDocumentDeliveryAttempts.FirstAsync();
        context.FinancialDocumentDeliveryAttempts.Remove(attempt);
        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        Assert.Contains("append-only", refusal.Message, StringComparison.Ordinal);
    }

    // ── What the administrator reads ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_administrators_page_lists_every_email_newest_first_with_its_attempts_and_the_pdfs_it_carried()
    {
        var (receipt, statement) = await DrawnAsync();
        await _harness.EmailPassAsync();
        _harness.Now = _harness.Now.AddMinutes(5);
        var admin = await AdminAsync();
        await _harness.RequestEmailAsync(receipt.Id, admin);

        var page = await PageAsync(receipt.Id);

        Assert.Equal(["Queued", "Sent"], page.Emails.Select(email => email.State));
        Assert.Equal(admin.Value, page.Emails[0].RequestedByAdminId);
        Assert.Equal("Ali Admin", page.Emails[0].RequestedByName);
        Assert.Null(page.Emails[1].RequestedByAdminId);
        Assert.Equal(["ar", "en"], page.Emails[1].Languages);
        Assert.Equal("rana@example.jo", page.Emails[1].Recipient);
        var attempt = Assert.Single(page.Emails[1].Attempts);
        Assert.Equal("Accepted", attempt.Outcome);
        var renditions = await _harness.RenditionsAsync();
        Assert.Equal(renditions.Single(rendition => rendition.DocumentId == receipt.Id && rendition.Language == Language.English).ContentSha256, attempt.EnglishPdfSha256);
        Assert.Equal(renditions.Single(rendition => rendition.DocumentId == receipt.Id && rendition.Language == Language.Arabic).ContentSha256, attempt.ArabicPdfSha256);

        // Only a receipt, never while one is queued.
        Assert.False(page.CanEmailAgain);
        Assert.False((await PageAsync(statement.Id)).CanEmailAgain);
        await _harness.EmailPassAsync();
        Assert.True((await PageAsync(receipt.Id)).CanEmailAgain);
    }

    [Fact]
    public async Task The_work_queue_lists_a_receipt_whose_latest_email_failed_or_waited_too_long_and_forgets_it_once_one_goes()
    {
        var (receipt, _) = await IssuedAsync();
        Assert.Equal(0, (await SummaryAsync()).Count);

        // Waiting for its PDF past the threshold.
        _harness.Now = _harness.Now.Add(_harness.EmailSettings.StaleAfter).AddMinutes(1);
        var stale = await SummaryAsync();
        Assert.Equal(1, stale.Count);
        Assert.Equal([receipt.Number], stale.Numbers);
        Assert.Equal(receipt.IssuedAt, stale.OldestQueuedAt);

        // Given up: still listed.
        await _harness.RenderPassAsync();
        _harness.EmailSettings.MaxSendAttempts = 1;
        _harness.Mail.Refusal = new InvalidOperationException("Resend refused the message (422): invalid sender");
        await _harness.EmailPassAsync();
        Assert.Equal(1, (await SummaryAsync()).Count);

        // Sent again, successfully: the failure is history, not work.
        _harness.Mail.Refusal = null;
        await _harness.RequestEmailAsync(receipt.Id);
        await _harness.EmailPassAsync();
        Assert.Equal(0, (await SummaryAsync()).Count);
    }

    [Fact]
    public async Task A_receipt_voided_after_its_email_failed_leaves_the_work_queue_which_nobody_could_otherwise_clear()
    {
        var (receipt, _) = await DrawnAsync();
        _harness.EmailSettings.MaxSendAttempts = 1;
        _harness.Mail.Refusal = new InvalidOperationException("Resend refused the message (422): invalid sender");
        await _harness.EmailPassAsync();
        Assert.Equal([receipt.Number], (await SummaryAsync()).Numbers);

        Assert.True((await _harness.VoidAsync(receipt.Id, "Wrong amount.")).IsSuccess);

        // It can never be emailed again; its correction has an email of its own, just queued.
        Assert.Equal(0, (await SummaryAsync()).Count);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>A sandbox payment's receipt and its booking's statement, issued.</summary>
    private async Task<(FinancialDocument Receipt, FinancialDocument Statement)> IssuedAsync()
    {
        await _harness.PaidAsync(PaymentProviders.Sandbox);
        await _harness.PassAsync();
        var documents = await _harness.DocumentsAsync();
        return (
            documents.Single(document => document.Type == FinancialDocumentType.PaymentReceipt),
            documents.Single(document => document.Type == FinancialDocumentType.BookingStatement));
    }

    /// <summary>Issued, and their PDFs drawn.</summary>
    private async Task<(FinancialDocument Receipt, FinancialDocument Statement)> DrawnAsync()
    {
        var issued = await IssuedAsync();
        await _harness.RenderPassAsync();
        return issued;
    }

    private async Task<Id> AdminAsync()
    {
        var admin = User.CreateAdmin(
            EmailAddress.Create("ali.admin@khadra.jo").Value,
            PhoneNumber.Create("0797654321").Value,
            PersonName.Create("Ali Admin").Value,
            PasswordHash.FromHash("hash"),
            _harness.Now);
        await using var context = _harness.NewContext();
        context.Users.Add(admin);
        await context.SaveChangesAsync();
        return admin.Id;
    }

    private async Task<AdminFinancialDocumentDto> PageAsync(Id documentId)
    {
        await using var context = _harness.NewContext();
        var handler = new AdminFinancialDocumentQueryHandlers(new FinancialDocumentReader(context), new BookingRepository(context), DocumentFixtures.Amman, _harness.EmailSettings);
        return (await handler.Handle(new GetAdminFinancialDocumentQuery(documentId), CancellationToken.None)).Value;
    }

    private async Task<FinancialDocumentEmailsSummary> SummaryAsync()
    {
        await using var context = _harness.NewContext();
        return await new FinancialDocumentReader(context).EmailsNotSentSummaryAsync(_harness.Now.Subtract(_harness.EmailSettings.StaleAfter));
    }
}
