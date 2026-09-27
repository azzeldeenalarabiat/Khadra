using Khadra.Application.Auditing;
using Khadra.Application.Bookings;
using Khadra.Application.Common;
using Khadra.Application.FinancialDocuments.Composition;
using Khadra.Application.FinancialDocuments.Issuance;
using Khadra.Application.FinancialDocuments.Queries;
using Khadra.Application.FinancialDocuments.ReadModels;
using Khadra.Application.FinancialDocuments.VoidFinancialDocument;
using Khadra.Application.Payments;
using Khadra.Domain.Auditing;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Domain.Fleet;
using Khadra.Domain.FinancialDocuments;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.Payments;
using Khadra.Domain.PlatformSettings;
using Khadra.Infrastructure.Persistence;
using Khadra.Infrastructure.Persistence.Repositories;
using Khadra.Infrastructure.Reporting;
using Khadra.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Khadra.Tests.Persistence;

/// <summary>
/// Issuing financial documents end to end on SQLite with the real repositories, readers and unit of work
/// (payments Phase 5): the settlement pass's issuing step exactly as <c>BookingSettlementService</c> runs it
/// — one query for the work, then a context per document, holds in a context of their own.
/// </summary>
public sealed class FinancialDocumentIssuanceTests : IDisposable
{
    private static readonly DateTimeOffset Start = IssuanceHarness.Start;

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly IssuanceHarness _harness;

    public FinancialDocumentIssuanceTests()
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

    // ── Issuing ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Sandbox_money_gets_a_TEST_receipt_and_then_its_statement()
    {
        var (booking, payment) = await _harness.PaidAsync(PaymentProviders.Sandbox);

        var outcomes = await _harness.PassAsync();

        Assert.Equal(["TEST-PAY-2026-000001", "TEST-STM-2026-000001"], outcomes.Select(outcome => outcome.Number ?? "(none)").ToArray());
        var documents = await _harness.DocumentsAsync();
        var receipt = documents.Single(document => document.Type == FinancialDocumentType.PaymentReceipt);
        Assert.Equal(payment.Id, receipt.SubjectId);
        Assert.Equal(booking.Id, receipt.BookingId);
        Assert.Equal(booking.CustomerId, receipt.CustomerId);
        Assert.True(receipt.IsTest);
        var statement = documents.Single(document => document.Type == FinancialDocumentType.BookingStatement);
        Assert.Equal(1, statement.Version);
        Assert.Equal(payment.AppliedAt, statement.CoversThrough);
        Assert.Equal(FinancialDocumentCause.PaymentCaptured, statement.Cause);
    }

    [Fact]
    public async Task Nothing_is_issued_twice()
    {
        await _harness.PaidAsync(PaymentProviders.Sandbox);
        await _harness.PassAsync();

        _harness.Now = _harness.Now.AddMinutes(1);
        var again = await _harness.PassAsync();

        // The statement is looked at again inside the late-commit margin and found unchanged; the receipt is not even a candidate.
        Assert.All(again, outcome => Assert.Null(outcome.Number));
        Assert.Equal(2, (await _harness.DocumentsAsync()).Count);
    }

    [Fact]
    public async Task The_clock_alone_never_issues_a_version()
    {
        var (booking, _) = await _harness.PaidAsync(PaymentProviders.Sandbox);
        await _harness.PassAsync();

        // Days later — windows closed, states moved on the clock — and nothing stored changed.
        _harness.Now = booking.Period.End.AddDays(5);
        var later = await _harness.PassAsync();

        Assert.Empty(later);
        Assert.Equal(2, (await _harness.DocumentsAsync()).Count);
    }

    [Fact]
    public async Task A_settled_refund_gets_its_receipt_and_a_new_statement_version()
    {
        var (booking, payment) = await _harness.PaidAsync(PaymentProviders.Sandbox, inFull: true, fee: 4.5m);
        await _harness.PassAsync();

        await _harness.ChangeAsync(async context =>
        {
            var tracked = await context.Bookings.Include(candidate => candidate.Handovers).SingleAsync(candidate => candidate.Id == booking.Id);
            var paid = await context.Payments.Include(candidate => candidate.Refunds).SingleAsync(candidate => candidate.Id == payment.Id);
            Assert.True(tracked.Cancel(BookingParty.Customer, tracked.CustomerId, null, _harness.Now).IsSuccess);
            var refund = BookingEndingRefunds.Record(tracked, paid, _harness.Now)!;
            refund.MarkSent("rf_1", _harness.Now.AddMinutes(1));
            refund.MarkSettled(_harness.Now.AddMinutes(2));
        });
        _harness.Now = _harness.Now.AddMinutes(3);

        var outcomes = await _harness.PassAsync();

        Assert.Equal(["TEST-RFD-2026-000001", "TEST-STM-2026-000002"], outcomes.Where(outcome => outcome.Number is not null).Select(outcome => outcome.Number ?? "(none)").ToArray());
        var documents = await _harness.DocumentsAsync();
        var receipt = documents.Single(document => document.Type == FinancialDocumentType.PaymentReceipt);
        var refundReceipt = documents.Single(document => document.Type == FinancialDocumentType.RefundReceipt);
        Assert.Equal(receipt.Id, refundReceipt.RelatedDocumentId);
        var statements = documents.Where(document => document.Type == FinancialDocumentType.BookingStatement).OrderBy(document => document.Version).ToList();
        Assert.Equal(2, statements.Count);
        Assert.Equal(statements[0].Id, statements[1].PreviousVersionId);
        Assert.Equal(FinancialDocumentCause.RefundSettled, statements[1].Cause);
        // The receipt it belongs to never changed: it is the same row, byte for byte.
        Assert.Equal(receipt.ContentSha256, FinancialDocument.Sha256(receipt.Snapshot));
    }

    [Fact]
    public async Task A_real_providers_money_is_numbered_without_the_prefix()
    {
        _harness.Issuer = DocumentFixtures.Issuer with { IsTestIdentity = false };
        await _harness.PaidAsync("TestProvider");

        var outcomes = await _harness.PassAsync();

        Assert.Equal(["PAY-2026-000001", "STM-2026-000001"], outcomes.Select(outcome => outcome.Number ?? "(none)").ToArray());
    }

    [Fact]
    public async Task A_test_identity_never_signs_real_money()
    {
        await _harness.PaidAsync("TestProvider");

        var outcomes = await _harness.PassAsync();

        Assert.All(outcomes, outcome => Assert.Equal(IssuanceHoldReason.IssuerNotConfigured, outcome.Hold!.Reason));
        Assert.Empty(await _harness.DocumentsAsync());
    }

    // ── Holds ──────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Without_an_issuer_every_document_waits_on_hold_and_is_issued_once_one_exists()
    {
        _harness.Issuer = null;
        await _harness.PaidAsync(PaymentProviders.Sandbox);

        var held = await _harness.PassAsync();
        Assert.Equal(2, held.Count);
        Assert.All(held, outcome => Assert.Equal(IssuanceHoldReason.IssuerNotConfigured, outcome.Hold!.Reason));
        Assert.Empty(await _harness.DocumentsAsync());
        var holds = await _harness.HoldsAsync();
        Assert.Equal(2, holds.Count(hold => !hold.IsResolved));

        // Waiting for its next attempt: the next pass does not even look.
        _harness.Now = _harness.Now.AddMinutes(1);
        Assert.Empty(await _harness.PassAsync());

        // An issuer arrives (a restart): the holds stop waiting at once.
        _harness.Issuer = DocumentFixtures.Issuer;
        var issued = await _harness.PassAsync();

        Assert.Equal(["TEST-PAY-2026-000001", "TEST-STM-2026-000001"], issued.Select(outcome => outcome.Number ?? "(none)").ToArray());
        Assert.All(await _harness.HoldsAsync(), hold => Assert.True(hold.IsResolved));
    }

    [Fact]
    public async Task Records_that_contradict_one_another_hold_the_statement_and_the_receipt_still_issues()
    {
        // Confirmed on a payment id no payment carries (ConfirmingPaymentMissing), and a stray capture on it.
        var (customer, dealer, vehicle) = await _harness.PartiesAsync();
        var booking = Build.Booking(_harness.Now, customerId: customer.Id, dealerId: dealer.Id, vehicleId: vehicle.Id);
        Assert.True(booking.Approve(Id.New(), _harness.Now).IsSuccess);
        Assert.True(booking.ConfirmDepositPaid(Id.New(), _harness.Now).IsSuccess);
        var stray = Payment.Open(booking.Id, booking.CustomerId, Money.Jod(18m), PaymentProviders.Sandbox, _harness.Now.AddMinutes(30), _harness.Now, PaymentPurpose.Deposit, Money.Jod(0m), true);
        Assert.True(stray.AttachProviderSession("sess_stray", "https://provider.test/checkout").IsSuccess);
        Assert.True(stray.Orphan(Money.Jod(18m), _harness.Now, "booking.not_awaiting_payment", _harness.Now).IsSuccess);
        await _harness.SaveAsync(booking, stray);

        var outcomes = await _harness.PassAsync();

        Assert.Equal("TEST-PAY-2026-000001", outcomes[0].Number);
        Assert.Equal(IssuanceHoldReason.RecordsNeedReview, outcomes[1].Hold!.Reason);
        var hold = Assert.Single(await _harness.HoldsAsync());
        Assert.Equal(FinancialDocumentType.BookingStatement, hold.DocumentType);
        Assert.Contains("ConfirmingPaymentMissing", hold.LastError, StringComparison.Ordinal);
        Assert.Equal(_harness.Now.Add(_harness.Settings().RetryInitialDelay), hold.NextAttemptAt);
    }

    [Fact]
    public async Task A_holds_retries_wait_longer_each_time()
    {
        _harness.Issuer = null;
        await _harness.PaidAsync(PaymentProviders.Sandbox);
        await _harness.PassAsync();

        await using var context = _harness.NewContext();
        var hold = await context.FinancialDocumentIssuanceHolds.FirstAsync();
        // A missing issuer waits the longest straight away: only configuration changes it.
        Assert.Equal(_harness.Now.Add(_harness.Settings().RetryMaxDelay), hold.NextAttemptAt);
        Assert.Equal(1, hold.Attempts);
    }

    // ── Being prepared ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task What_is_owed_and_not_issued_is_being_prepared_until_it_is()
    {
        var (booking, payment) = await _harness.PaidAsync(PaymentProviders.Sandbox);

        await using (var context = _harness.NewContext())
        {
            var pending = await new FinancialDocumentReader(context).PendingForBookingAsync(booking.Id);
            Assert.Equal([FinancialDocumentType.PaymentReceipt, FinancialDocumentType.BookingStatement], pending.Select(entry => entry.Type).ToArray());
            Assert.Equal(payment.Id, pending[0].SubjectId);
        }

        await _harness.PassAsync();

        await using (var context = _harness.NewContext())
            Assert.Empty(await new FinancialDocumentReader(context).PendingForBookingAsync(booking.Id));
    }

    // ── Voids and corrections ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_void_issues_its_correction_in_the_same_transaction_and_the_sweep_never_reissues_it()
    {
        await _harness.PaidAsync(PaymentProviders.Sandbox);
        await _harness.PassAsync();
        var receipt = (await _harness.DocumentsAsync()).Single(document => document.Type == FinancialDocumentType.PaymentReceipt);

        var voided = await _harness.VoidAsync(receipt.Id, "The office's name was wrong.");

        Assert.True(voided.IsSuccess, voided.IsFailure ? voided.Error.Code : null);
        Assert.Equal("TEST-PAY-2026-000002", voided.Value.ReplacementNumber);
        var documents = await _harness.DocumentsAsync();
        var correction = documents.Single(document => document.Id.Value == voided.Value.ReplacementDocumentId);
        Assert.Equal(2, correction.Version);
        Assert.Equal(receipt.Id, correction.PreviousVersionId);
        Assert.Equal(FinancialDocumentCause.Correction, correction.Cause);
        Assert.Equal(receipt.OccurredAt, correction.OccurredAt);

        await using (var context = _harness.NewContext())
        {
            var record = Assert.Single(await context.FinancialDocumentVoids.ToListAsync());
            Assert.Equal(receipt.Id, record.DocumentId);
            Assert.Equal("The office's name was wrong.", record.Reason);
            var audit = Assert.Single(await context.AuditEntries.ToListAsync());
            Assert.Equal(AuditAction.FinancialDocumentVoided, audit.Action);
            Assert.Equal(AuditEntityType.FinancialDocument, audit.EntityType);
            Assert.Equal(receipt.Number, audit.SubjectLabel);
            Assert.Equal(receipt.Number, audit.PreviousValue);
            Assert.Equal(correction.Number, audit.NewValue);

            var reader = new FinancialDocumentReader(context);
            Assert.Equal(FinancialDocumentStatus.Voided, (await reader.GetAsync(receipt.Id))!.Status);
            Assert.Equal(FinancialDocumentStatus.Current, (await reader.GetAsync(correction.Id))!.Status);
        }

        // The family has rows: the sweep never issues a voided receipt again.
        _harness.Now = _harness.Now.AddMinutes(1);
        Assert.All(await _harness.PassAsync(), outcome => Assert.Null(outcome.Number));
        Assert.Equal(3, (await _harness.DocumentsAsync()).Count);
    }

    [Fact]
    public async Task Only_the_current_version_can_be_voided_and_only_once()
    {
        await _harness.PaidAsync(PaymentProviders.Sandbox);
        await _harness.PassAsync();
        var receipt = (await _harness.DocumentsAsync()).Single(document => document.Type == FinancialDocumentType.PaymentReceipt);
        Assert.True((await _harness.VoidAsync(receipt.Id, "Wrong.")).IsSuccess);

        Assert.Equal(FinancialDocumentErrors.AlreadyVoided.Code, (await _harness.VoidAsync(receipt.Id, "Again.")).Error.Code);
        var statement = (await _harness.DocumentsAsync()).Single(document => document.Type == FinancialDocumentType.BookingStatement);
        Assert.True((await _harness.VoidAsync(statement.Id, "Wrong total.")).IsSuccess);
        Assert.Equal(FinancialDocumentErrors.AlreadyVoided.Code, (await _harness.VoidAsync(statement.Id, "Wrong.")).Error.Code);
        Assert.Equal(FinancialDocumentErrors.NotFound.Code, (await _harness.VoidAsync(Id.New(), "Wrong.")).Error.Code);
    }

    [Fact]
    public async Task A_superseded_version_is_history_and_cannot_be_voided()
    {
        var (booking, payment) = await _harness.PaidAsync(PaymentProviders.Sandbox, inFull: true);
        await _harness.PassAsync();
        await _harness.ChangeAsync(async context =>
        {
            var tracked = await context.Bookings.Include(candidate => candidate.Handovers).SingleAsync(candidate => candidate.Id == booking.Id);
            var paid = await context.Payments.Include(candidate => candidate.Refunds).SingleAsync(candidate => candidate.Id == payment.Id);
            Assert.True(tracked.Cancel(BookingParty.Customer, tracked.CustomerId, null, _harness.Now).IsSuccess);
            BookingEndingRefunds.Record(tracked, paid, _harness.Now);
        });
        _harness.Now = _harness.Now.AddMinutes(1);
        await _harness.PassAsync();
        var first = (await _harness.DocumentsAsync()).Single(document => document.Type == FinancialDocumentType.BookingStatement && document.Version == 1);

        Assert.Equal(FinancialDocumentErrors.NotCurrent.Code, (await _harness.VoidAsync(first.Id, "Wrong.")).Error.Code);
    }

    [Fact]
    public async Task No_correction_means_no_void()
    {
        await _harness.PaidAsync(PaymentProviders.Sandbox);
        await _harness.PassAsync();
        var receipt = (await _harness.DocumentsAsync()).Single(document => document.Type == FinancialDocumentType.PaymentReceipt);
        _harness.Issuer = null;

        var refused = await _harness.VoidAsync(receipt.Id, "Wrong.");

        Assert.Equal(FinancialDocumentErrors.CorrectionIssuerNotConfigured.Code, refused.Error.Code);
        await using var context = _harness.NewContext();
        Assert.Empty(await context.FinancialDocumentVoids.ToListAsync());
        Assert.Empty(await context.AuditEntries.ToListAsync());
    }

    // ── Numbers ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_number_taken_by_a_transaction_that_rolled_back_is_taken_again()
    {
        await using (var context = _harness.NewContext())
        {
            var unitOfWork = IssuanceHarness.UnitOfWork(context);
            var issuing = new FinancialDocumentIssuing(new FinancialDocumentSeriesCounter(context), new FinancialDocumentRepository(context), DocumentFixtures.Amman);

            await Assert.ThrowsAsync<DocumentCompositionException>(() => unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                await issuing.IssueAsync(
                    FinancialDocumentType.PaymentReceipt, PaymentProviders.Sandbox, _harness.Now, 1, null, false,
                    _ => throw new InvalidOperationException("A defect in composition."), token);
                await unitOfWork.SaveChangesAsync(token);
            }));
        }

        await _harness.PaidAsync(PaymentProviders.Sandbox);
        var outcomes = await _harness.PassAsync();

        Assert.Equal("TEST-PAY-2026-000001", outcomes[0].Number);
    }

    [Fact]
    public async Task A_number_is_only_ever_taken_inside_a_transaction()
    {
        await using var context = _harness.NewContext();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new FinancialDocumentSeriesCounter(context).TakeNextAsync("TEST-PAY-2026", _harness.Now));
    }

    [Fact]
    public async Task A_series_follows_the_Amman_issue_year()
    {
        // 22:30 UTC on 31 December is already 1 January in Amman.
        _harness.Now = new DateTimeOffset(2026, 12, 31, 22, 30, 0, TimeSpan.Zero);
        await _harness.PaidAsync(PaymentProviders.Sandbox);

        var outcomes = await _harness.PassAsync();

        Assert.Equal("TEST-PAY-2027-000001", outcomes[0].Number);
    }

    // ── The late-commit margin ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_fact_committed_after_a_statement_that_missed_it_is_caught_inside_the_margin()
    {
        var (booking, payment) = await _harness.PaidAsync(PaymentProviders.Sandbox, inFull: true);
        var endedAt = _harness.Now;
        await _harness.ChangeAsync(async context =>
        {
            var tracked = await context.Bookings.Include(candidate => candidate.Handovers).SingleAsync(candidate => candidate.Id == booking.Id);
            var paid = await context.Payments.Include(candidate => candidate.Refunds).SingleAsync(candidate => candidate.Id == payment.Id);
            Assert.True(tracked.Cancel(BookingParty.Customer, tracked.CustomerId, null, endedAt).IsSuccess);
            BookingEndingRefunds.Record(tracked, paid, endedAt);
        });
        _harness.Now = endedAt.AddMinutes(2);
        await _harness.PassAsync();
        var first = (await _harness.DocumentsAsync()).Single(document => document.Type == FinancialDocumentType.BookingStatement);
        Assert.Equal(endedAt, first.CoversThrough);

        // A settlement stamped BEFORE that statement's coverage, committed after it was issued — a handler
        // that read its clock, took its time, and saved late.
        await _harness.ChangeAsync(async context =>
        {
            var paid = await context.Payments.Include(candidate => candidate.Refunds).SingleAsync(candidate => candidate.Id == payment.Id);
            var refund = paid.Refunds.Single();
            refund.MarkSent("rf_1", endedAt.AddSeconds(-40));
            refund.MarkSettled(endedAt.AddSeconds(-30));
        });
        _harness.Now = _harness.Now.AddMinutes(1);

        var outcomes = await _harness.PassAsync();

        Assert.Contains(outcomes, outcome => outcome.Number == "TEST-STM-2026-000002");
    }

    // ── Reading ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_customer_reads_only_their_own_documents_and_the_office_gets_none()
    {
        var (booking, _) = await _harness.PaidAsync(PaymentProviders.Sandbox);
        await _harness.PassAsync();
        var receipt = (await _harness.DocumentsAsync()).Single(document => document.Type == FinancialDocumentType.PaymentReceipt);

        await using var context = _harness.NewContext();
        var handlers = new CustomerFinancialDocumentQueryHandlers(
            new FinancialDocumentReader(context),
            new BookingRepository(context),
            new BookingPartyResolver(new DealerRepository(context)));

        var own = await handlers.Handle(new GetMyFinancialDocumentQuery(booking.CustomerId, receipt.Id), CancellationToken.None);
        Assert.True(own.IsSuccess);
        Assert.Equal(receipt.Number, own.Value.Number);
        Assert.Equal("Payment receipt", own.Value.Title.En);
        Assert.Equal(receipt.Snapshot, own.Value.Snapshot.GetRawText());

        var stranger = await handlers.Handle(new GetMyFinancialDocumentQuery(Id.New(), receipt.Id), CancellationToken.None);
        Assert.Equal(FinancialDocumentErrors.NotFound.Code, stranger.Error.Code);

        var mine = await handlers.Handle(new ListMyFinancialDocumentsQuery(booking.CustomerId, null, 1, 20), CancellationToken.None);
        Assert.Equal(2, mine.Value.TotalCount);
        var theirs = await handlers.Handle(new ListMyFinancialDocumentsQuery(Id.New(), null, 1, 20), CancellationToken.None);
        Assert.Equal(0, theirs.Value.TotalCount);

        var dealer = await context.Dealers.SingleAsync();
        var office = await handlers.Handle(new GetBookingFinancialDocumentsQuery(dealer.OwnerUserId, booking.Id), CancellationToken.None);
        Assert.True(office.IsSuccess);
        Assert.Empty(office.Value.Documents);
        Assert.Empty(office.Value.BeingPrepared);

        var customer = await handlers.Handle(new GetBookingFinancialDocumentsQuery(booking.CustomerId, booking.Id), CancellationToken.None);
        Assert.Equal(2, customer.Value.Documents.Count);
    }

    [Fact]
    public async Task The_office_and_the_car_are_named_even_after_they_were_deleted()
    {
        var (booking, _) = await _harness.PaidAsync(PaymentProviders.Sandbox);
        await _harness.ChangeAsync(async context =>
        {
            var dealer = await context.Dealers.SingleAsync();
            var vehicle = await context.Vehicles.SingleAsync();
            Assert.True(vehicle.Delete(_harness.Now).IsSuccess);
            Assert.True(dealer.Delete(_harness.Now).IsSuccess);
        });

        var outcomes = await _harness.PassAsync();

        Assert.All(outcomes, outcome => Assert.NotNull(outcome.Number));
        var statement = (await _harness.DocumentsAsync()).Single(document => document.Type == FinancialDocumentType.BookingStatement);
        var snapshot = DocumentFixtures.Parse(statement.Snapshot);
        Assert.Equal("Petra Rentals", snapshot.GetProperty("office").GetProperty("name").GetString());
        Assert.Equal("Corolla", snapshot.GetProperty("booking").GetProperty("vehicle").GetProperty("model").GetString());
        Assert.Equal(booking.Reference.Value, statement.BookingReference);
    }

    [Fact]
    public async Task The_administrators_list_filters_by_standing()
    {
        await _harness.PaidAsync(PaymentProviders.Sandbox);
        await _harness.PassAsync();
        var receipt = (await _harness.DocumentsAsync()).Single(document => document.Type == FinancialDocumentType.PaymentReceipt);
        Assert.True((await _harness.VoidAsync(receipt.Id, "Wrong.")).IsSuccess);

        await using var context = _harness.NewContext();
        var handlers = new AdminFinancialDocumentQueryHandlers(new FinancialDocumentReader(context), new BookingRepository(context), DocumentFixtures.Amman);

        var voided = await handlers.Handle(new ListAdminFinancialDocumentsQuery(null, "Voided", null, null, null, null, 1, 20), CancellationToken.None);
        var current = await handlers.Handle(new ListAdminFinancialDocumentsQuery(null, "Current", null, null, null, null, 1, 20), CancellationToken.None);
        var byNumber = await handlers.Handle(new ListAdminFinancialDocumentsQuery(null, null, "test-pay-2026-000001", null, null, null, 1, 20), CancellationToken.None);

        Assert.Equal(receipt.Number, Assert.Single(voided.Value.Items).Number);
        Assert.Equal(2, current.Value.TotalCount);
        Assert.True(Assert.Single(byNumber.Value.Items).IsTest);

        var page = await handlers.Handle(new GetAdminFinancialDocumentQuery(receipt.Id), CancellationToken.None);
        Assert.Equal("Wrong.", page.Value.Void!.Reason);
        Assert.Equal("TEST-PAY-2026-000002", page.Value.Void.ReplacedBy!.Number);
        Assert.Equal("TEST-PAY-2026-000002", page.Value.Document.Voided!.ReplacedBy!.Number);
        Assert.Equal(PaymentProviders.Sandbox, page.Value.Provider);
    }

    // ── Persistence ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Every_column_round_trips_and_the_snapshot_comes_back_exactly()
    {
        await _harness.PaidAsync(PaymentProviders.Sandbox);
        await _harness.PassAsync();

        await using var context = _harness.NewContext();
        foreach (var document in await context.FinancialDocuments.AsNoTracking().ToListAsync())
        {
            Assert.Equal(document.ContentSha256, FinancialDocument.Sha256(document.Snapshot));
            Assert.Equal(1, document.SnapshotSchemaVersion);
            Assert.Equal(1, document.CalculatorVersion);
            Assert.Equal(PaymentProviders.Sandbox, document.Provider);
            Assert.NotNull(document.HeadlineAmount);
            Assert.Equal("JOD", document.HeadlineAmount.CurrencyCode);
            Assert.False(string.IsNullOrWhiteSpace(document.BookingReference));
        }
    }

    [Fact]
    public async Task A_document_and_a_void_can_be_neither_changed_nor_deleted()
    {
        await _harness.PaidAsync(PaymentProviders.Sandbox);
        await _harness.PassAsync();
        var receipt = (await _harness.DocumentsAsync()).Single(document => document.Type == FinancialDocumentType.PaymentReceipt);
        Assert.True((await _harness.VoidAsync(receipt.Id, "Wrong.")).IsSuccess);

        await using (var context = _harness.NewContext())
        {
            var document = await context.FinancialDocuments.SingleAsync(candidate => candidate.Id == receipt.Id);
            context.Entry(document).Property(nameof(FinancialDocument.Provider)).CurrentValue = "ALTERED";
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        }

        await using (var context = _harness.NewContext())
        {
            context.FinancialDocuments.Remove(await context.FinancialDocuments.SingleAsync(candidate => candidate.Id == receipt.Id));
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        }

        await using (var context = _harness.NewContext())
        {
            context.FinancialDocumentVoids.Remove(await context.FinancialDocumentVoids.SingleAsync());
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task A_family_version_and_a_number_are_each_unique()
    {
        await _harness.PaidAsync(PaymentProviders.Sandbox);
        await _harness.PassAsync();
        var receipt = (await _harness.DocumentsAsync()).Single(document => document.Type == FinancialDocumentType.PaymentReceipt);

        await using var context = _harness.NewContext();
        var twin = FinancialDocument.Issue(
            new FinancialDocumentDraft(
                receipt.Type, receipt.SubjectId, 1, null, null, receipt.BookingId, receipt.BookingReference, receipt.CustomerId,
                receipt.DealerId, receipt.PaymentId, null, receipt.Cause, receipt.OccurredAt, null, null, receipt.HeadlineAmount,
                receipt.Provider, 1, 1, receipt.Snapshot),
            "TEST-PAY-2026-000099",
            _harness.Now);
        context.FinancialDocuments.Add(twin);

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }
}
