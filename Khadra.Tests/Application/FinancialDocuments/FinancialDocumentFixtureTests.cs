using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Khadra.Application.Common;
using Khadra.Application.FinancialDocuments.Composition;
using Khadra.Application.FinancialDocuments.Queries;
using Khadra.Application.FinancialDocuments.ReadModels;
using Khadra.Application.Payments;
using Khadra.Application.Payments.Financials;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.Disputes;
using Khadra.Domain.FinancialDocuments;
using Khadra.Domain.Payments;
using Khadra.Tests.Support;
using static Khadra.Tests.Support.DocumentFixtures;

namespace Khadra.Tests.Application.FinancialDocuments;

/// <summary>
/// The shared contract fixture (payments Phase 5b, decision D5): <c>docs/contracts/financial-documents-v1.json</c>
/// holds customer pages of every shape the version 1 grammar has, composed by the REAL composer and shaped as
/// the customer's endpoint serialises them. The website's, the app's and the console's tests all read it, so a
/// change here that would break an installed app fails on the server, not on a customer's phone.
/// </summary>
/// <remarks>
/// <para>
/// When this test fails, the composer no longer writes what the committed file holds. A change to the WORDS
/// of a permanent record is meant to be seen in review: regenerate with
/// <c>KHADRA_REGENERATE_CONTRACT_FIXTURES=1 dotnet test --filter FinancialDocumentFixtureTests</c> (PowerShell:
/// <c>$env:KHADRA_REGENERATE_CONTRACT_FIXTURES='1'; dotnet test --filter FinancialDocumentFixtureTests</c>, then
/// <c>Remove-Item env:KHADRA_REGENERATE_CONTRACT_FIXTURES</c>) and read the diff. A change to the GRAMMAR is a
/// new snapshot schema version, published app first (<c>docs/contracts/README.md</c>) — a new file beside this
/// one, never an edit to it.
/// </para>
/// <para>
/// Ids and booking references are minted at random by the domain, so they are renumbered in order of first
/// appearance before the comparison; everything else — every figure, instant and word — is exactly what the
/// composer wrote. Line endings are compared as <c>\n</c>, whatever git checked the file out with.
/// </para>
/// </remarks>
public sealed partial class FinancialDocumentFixtureTests
{
    private const string RegenerateSwitch = "KHADRA_REGENERATE_CONTRACT_FIXTURES";

    /// <summary>The four kinds of line value in version 1: a line carries exactly one.</summary>
    private static readonly string[] ValueKinds = ["money", "instant", "text", "plain"];

    private static readonly FinancialDocumentComposer Composer = DocumentFixtures.Composer();

    private static readonly JsonSerializerOptions FixtureJson = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        NewLine = "\n",
        // Readable in review: Arabic and the direction isolates stay as they are, not as \u escapes.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    [Fact]
    public void The_shared_fixture_is_what_the_server_writes_today()
    {
        var generated = Generate();
        Assert.Equal(generated, Generate());

        var path = RepositoryRoot.File("docs", "contracts", "financial-documents-v1.json");
        if (Environment.GetEnvironmentVariable(RegenerateSwitch) == "1")
            File.WriteAllText(path, generated, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        Assert.True(File.Exists(path), $"{path} is missing. Create it with {RegenerateSwitch}=1.");
        var committed = File.ReadAllText(path).ReplaceLineEndings("\n");
        if (!string.Equals(committed, generated, StringComparison.Ordinal))
            throw new Xunit.Sdk.XunitException(Difference(committed, generated));
    }

    [Fact]
    public void The_shared_fixture_covers_the_whole_grammar()
    {
        using var fixture = JsonDocument.Parse(Generate());
        var pages = fixture.RootElement.GetProperty("documents").EnumerateArray().Select(entry => entry.GetProperty("page")).ToList();

        var kinds = new HashSet<string>(StringComparer.Ordinal);
        var unlabelled = 0;
        foreach (var page in pages)
        {
            Assert.Equal(SnapshotJson.SchemaVersion, page.GetProperty("snapshotSchemaVersion").GetInt32());
            foreach (var section in page.GetProperty("snapshot").GetProperty("content").GetProperty("sections").EnumerateArray())
            {
                foreach (var line in section.GetProperty("lines").EnumerateArray())
                {
                    // Counted as the readers count them (docs/contracts/README.md): a key holding null is no value.
                    var values = ValueKinds
                        .Where(kind => line.TryGetProperty(kind, out var value) && value.ValueKind != JsonValueKind.Null)
                        .ToList();
                    kinds.Add(Assert.Single(values));
                    if (line.GetProperty("label").ValueKind == JsonValueKind.Null)
                        unlabelled++;
                }
            }
        }

        Assert.Equal(["instant", "money", "plain", "text"], kinds.Order(StringComparer.Ordinal).ToArray());
        Assert.True(unlabelled > 0, "No unlabelled line: the fixture no longer covers a line with no label.");
        Assert.Equal(
            ["BookingStatement", "PaymentReceipt", "RefundReceipt"],
            pages.Select(page => page.GetProperty("type").GetString()!).Distinct().Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(
            ["Current", "Superseded", "Voided"],
            pages.Select(page => page.GetProperty("status").GetString()!).Distinct().Order(StringComparer.Ordinal).ToArray());
        Assert.Contains(pages, page => page.GetProperty("snapshot").GetProperty("document").GetProperty("isCorrection").GetBoolean());
        Assert.Contains(pages, page => page.GetProperty("voided").ValueKind == JsonValueKind.Object);
        Assert.Contains(pages, page => page.GetProperty("links").GetProperty("paymentReceipt").ValueKind == JsonValueKind.Object);
        Assert.Contains(pages, page => page.GetProperty("links").GetProperty("refundReceipts").GetArrayLength() > 0);
        foreach (var cause in new[] { "PaymentCaptured", "RefundSettled", "DisputeResolved", "CashRecorded", "Correction" })
            Assert.Contains(pages, page => page.GetProperty("cause").GetString() == cause);
    }

    // ── The documents ──────────────────────────────────────────────────────────────────────────────

    /// <summary>The fixture, as the committed file holds it: ids renumbered, <c>\n</c> line endings, a final newline.</summary>
    private static string Generate()
    {
        var customer = Id.New();
        var issued = new List<Issued>();
        var voids = new Dictionary<Id, FinancialDocumentVoidRecord>();

        // A — a deposit with a fee. Its receipt is voided and corrected; its statement is superseded when the
        // office records the balance collected in cash at pickup.
        var start = Build.Now;
        var (depositBooking, deposit) = Build.PaidBooking(fee: 1.5m, now: start, customerId: customer);
        var depositParties = PartiesOf(depositBooking);
        var depositReceipt = Issue(issued, "TEST-PAY-2026-000001", start.AddMinutes(1), stamp => Composer.PaymentReceipt(
            new PaymentReceiptFacts(Issuer, depositParties, depositBooking, deposit, [deposit]), stamp));
        var firstStatement = Issue(issued, "TEST-STM-2026-000001", start.AddMinutes(1), stamp => Composer.Statement(
            new StatementFacts(
                Issuer, depositParties, depositBooking,
                BookingFinancialsCalculator.Calculate(depositBooking, [deposit], [], false, stamp.IssuedAt),
                StatementCheckpoints.Of(depositBooking, [deposit], []), false,
                [Reference(depositReceipt)]),
            stamp, deposit.Provider));

        var voidedAt = start.AddDays(1);
        voids[depositReceipt.Id] = new FinancialDocumentVoidRecord(
            depositReceipt.Id, voidedAt, Id.New(), "Fixture Administrator", "Issued with the wrong office location.");
        var correction = Issue(
            issued, "TEST-PAY-2026-000005", voidedAt,
            stamp => Composer.PaymentReceipt(new PaymentReceiptFacts(Issuer, depositParties, depositBooking, deposit, [deposit]), stamp),
            previous: depositReceipt,
            isCorrection: true);

        var pickup = depositBooking.Period.Start;
        Must(depositBooking.RecordPickup(BookingParty.Dealer, Id.New(), pickup, cashCollected: Money.Jod(depositBooking.Pricing.BalanceDue.Amount)).IsSuccess, "the pickup with cash");
        var cashStatement = Issue(
            issued, "TEST-STM-2026-000003", pickup.AddMinutes(1),
            stamp => Composer.Statement(
                new StatementFacts(
                    Issuer, depositParties, depositBooking,
                    BookingFinancialsCalculator.Calculate(depositBooking, [deposit], [], false, stamp.IssuedAt),
                    StatementCheckpoints.Of(depositBooking, [deposit], []), false,
                    [Reference(correction)]),
                stamp, deposit.Provider),
            previous: firstStatement);

        // B — paid in full with a fee, then cancelled inside the free window: the whole payment comes back.
        var paidAt = start.AddHours(2);
        var (freeBooking, full) = Build.PaidBooking(inFull: true, fee: 4.5m, now: paidAt, customerId: customer);
        var freeParties = PartiesOf(freeBooking);
        var fullReceipt = Issue(issued, "TEST-PAY-2026-000002", paidAt.AddMinutes(1), stamp => Composer.PaymentReceipt(
            new PaymentReceiptFacts(Issuer, freeParties, freeBooking, full, [full]), stamp));
        var cancelledAt = Earliest(paidAt.AddMinutes(30), freeBooking.FreeCancellationDeadline!.Value.AddMinutes(-1));
        Must(freeBooking.Cancel(BookingParty.Customer, freeBooking.CustomerId, null, cancelledAt).IsSuccess, "the free cancellation");
        var wholeRefund = BookingEndingRefunds.Record(freeBooking, full, cancelledAt)!;
        Settle(wholeRefund, cancelledAt.AddMinutes(2));
        var wholeRefundReceipt = Issue(issued, "TEST-RFD-2026-000001", cancelledAt.AddMinutes(3), stamp => Composer.RefundReceipt(
            new RefundReceiptFacts(Issuer, freeParties, freeBooking, full, wholeRefund, new DocumentReference(fullReceipt.Id, fullReceipt.Number), null), stamp));

        // C — money captured and never applied: described as being refunded whole, with no booking arithmetic.
        var strayAt = start.AddHours(4);
        var strayBooking = Build.ApprovedBooking(now: strayAt, customerId: customer);
        var stray = Payment.Open(
            strayBooking.Id, strayBooking.CustomerId, Money.Jod(18m), "TestProvider", strayAt.AddMinutes(30), strayAt,
            PaymentPurpose.Deposit, Money.ZeroIn("JOD"), true);
        Must(stray.AttachProviderSession("sess_fixture_stray", "https://provider.test/checkout").IsSuccess, "the stray checkout");
        Must(stray.Orphan(Money.Jod(18m), strayAt.AddMinutes(5), "booking.not_awaiting_payment", strayAt.AddMinutes(5)).IsSuccess, "the orphaned capture");
        var strayReceipt = Issue(issued, "TEST-PAY-2026-000003", strayAt.AddMinutes(6), stamp => Composer.PaymentReceipt(
            new PaymentReceiptFacts(Issuer, PartiesOf(strayBooking), strayBooking, stray, [stray]), stamp));

        // D — paid in full, cancelled late, the money above the deposit refunded, and a dispute deciding the
        // deposit: 13.000 back to the customer, 5.000 to the office. Its statement is issued on the decision,
        // with that refund still on its way; the refund's own receipt follows when it settles.
        var disputedAt = start.AddHours(6);
        var (disputedBooking, disputed) = Build.PaidBooking(inFull: true, fee: 4.5m, now: disputedAt, customerId: customer);
        var disputedParties = PartiesOf(disputedBooking);
        var disputedReceipt = Issue(issued, "TEST-PAY-2026-000004", disputedAt.AddMinutes(1), stamp => Composer.PaymentReceipt(
            new PaymentReceiptFacts(Issuer, disputedParties, disputedBooking, disputed, [disputed]), stamp));
        var lateAt = disputedBooking.FreeCancellationDeadline!.Value.AddMinutes(1);
        Must(disputedBooking.Cancel(BookingParty.Customer, disputedBooking.CustomerId, null, lateAt).IsSuccess, "the late cancellation");
        var aboveDeposit = BookingEndingRefunds.Record(disputedBooking, disputed, lateAt)!;
        Settle(aboveDeposit, lateAt.AddMinutes(5));
        var aboveDepositReceipt = Issue(issued, "TEST-RFD-2026-000002", lateAt.AddMinutes(6), stamp => Composer.RefundReceipt(
            new RefundReceiptFacts(Issuer, disputedParties, disputedBooking, disputed, aboveDeposit, new DocumentReference(disputedReceipt.Id, disputedReceipt.Number), null), stamp));
        var ticket = DisputeTicket.Open(disputedBooking.Id, disputedBooking.CustomerId, BookingParty.Customer, "Charged twice.", TimeSpan.FromHours(48), lateAt.AddHours(1)).Value;
        Must(ticket.Resolve(DisputeResolution.Create(
            DepositDisposition.Create(Money.Jod(18m), Money.Jod(13m), Money.Jod(0m), Money.Jod(5m)).Value,
            null, null, "Split.", Id.New(), lateAt.AddHours(2)).Value).IsSuccess, "the dispute's resolution");
        Must(disputed.RequestRefund(Money.Jod(13m), ticket.Id, lateAt.AddHours(2)).IsSuccess, "the dispute's refund");
        var decidedStatement = Issue(issued, "TEST-STM-2026-000002", lateAt.AddHours(2).AddMinutes(1), stamp => Composer.Statement(
            new StatementFacts(
                Issuer, disputedParties, disputedBooking,
                BookingFinancialsCalculator.Calculate(disputedBooking, [disputed], [ticket], false, stamp.IssuedAt),
                StatementCheckpoints.Of(disputedBooking, [disputed], [ticket]), true,
                [Reference(disputedReceipt), Reference(aboveDepositReceipt)]),
            stamp, disputed.Provider));
        var share = disputed.Refunds.Single(refund => refund.Reason == RefundReason.DisputeResolution);
        Settle(share, lateAt.AddHours(3));
        var shareReceipt = Issue(issued, "TEST-RFD-2026-000003", lateAt.AddHours(3).AddMinutes(1), stamp => Composer.RefundReceipt(
            new RefundReceiptFacts(Issuer, disputedParties, disputedBooking, disputed, share, new DocumentReference(disputedReceipt.Id, disputedReceipt.Number), ticket.ClosedAt), stamp));

        // The records, with their standing worked out as the reader works it out.
        var records = issued.ToDictionary(document => document.Id, document => document.Record(
            voided: voids.ContainsKey(document.Id),
            superseded: issued.Any(other => other.PreviousId == document.Id)));
        // Linked exactly as GetMyFinancialDocumentQuery links them: the frozen related document, and for a
        // payment receipt the current receipt of each refund made from its payment.
        FinancialDocumentDto Page(Issued document)
        {
            var family = issued.Where(other => other.Family == document.Family).Select(other => records[other.Id]).ToList();
            var paymentReceipt = document.Draft.RelatedDocumentId is { } related ? records[related] : null;
            IReadOnlyList<FinancialDocumentRecord> refundReceipts = document.Draft.Type == FinancialDocumentType.PaymentReceipt
                ? [
                    .. issued
                        .Where(other => other.Draft.Type == FinancialDocumentType.RefundReceipt && other.Draft.PaymentId == document.Draft.PaymentId)
                        .Select(other => records[other.Id])
                        .GroupBy(candidate => candidate.SubjectId)
                        .Select(versions => versions.MaxBy(candidate => candidate.Version)!)
                        .OrderBy(candidate => candidate.OccurredAt)
                        .ThenBy(candidate => candidate.Number, StringComparer.Ordinal),
                ]
                : [];
            return FinancialDocumentPages.Build(records[document.Id], family, paymentReceipt, refundReceipts, voids.GetValueOrDefault(document.Id));
        }

        var rows = issued
            .Select(document => records[document.Id])
            .OrderByDescending(record => record.IssuedAt)
            .ThenByDescending(record => record.Number, StringComparer.Ordinal)
            .Select(FinancialDocumentListItem.From)
            .ToList();
        var fixture = new
        {
            about = "Customer pages of every shape the version 1 grammar has, composed by the server's own composer "
                + "and shaped as GET /api/v1/financial-documents/{id} serialises them. Generated by "
                + "Khadra.Tests FinancialDocumentFixtureTests — never edited by hand. Ids and references are renumbered.",
            schemaVersion = SnapshotJson.SchemaVersion,
            documents = new object[]
            {
                new { name = "payment-receipt-deposit-voided", page = Page(depositReceipt) },
                new { name = "payment-receipt-deposit-correction", page = Page(correction) },
                new { name = "booking-statement-superseded", page = Page(firstStatement) },
                new { name = "booking-statement-cash-at-handover", page = Page(cashStatement) },
                new { name = "payment-receipt-paid-in-full", page = Page(fullReceipt) },
                new { name = "refund-receipt-free-cancellation", page = Page(wholeRefundReceipt) },
                new { name = "payment-receipt-not-applied", page = Page(strayReceipt) },
                new { name = "refund-receipt-dispute-decision", page = Page(shareReceipt) },
                new { name = "booking-statement-dispute-decided", page = Page(decidedStatement) },
            },
            myDocuments = new PagedResult<FinancialDocumentListItem>(rows, 1, 20, rows.Count),
            bookingDocuments = new BookingFinancialDocumentsDto(
                freeBooking.Id.Value,
                [.. rows.Where(row => row.BookingId == freeBooking.Id.Value)],
                [new PendingFinancialDocumentDto(FinancialDocumentType.BookingStatement.Name, freeBooking.Id.Value, wholeRefund.SettledAt!.Value)]),
        };

        return Renumber(JsonSerializer.Serialize(fixture, FixtureJson)) + "\n";
    }

    private static Issued Issue(
        List<Issued> issued,
        string number,
        DateTimeOffset issuedAt,
        Func<DocumentStamp, FinancialDocumentDraft> compose,
        Issued? previous = null,
        bool isCorrection = false)
    {
        var stamp = previous is null
            ? DocumentStamp.First(number, issuedAt)
            : new DocumentStamp(number, issuedAt, previous.Version + 1, new DocumentReference(previous.Id, previous.Number), isCorrection);
        var document = new Issued(Id.New(), number, issuedAt, stamp.Version, previous?.Id, previous?.Family ?? Id.New(), compose(stamp));
        issued.Add(document);
        return document;
    }

    private static ReceiptReference Reference(Issued receipt) => new(receipt.Draft.Type, receipt.Id, receipt.Number);

    private static DateTimeOffset Earliest(DateTimeOffset first, DateTimeOffset second) => first <= second ? first : second;

    private static void Settle(Refund refund, DateTimeOffset at)
    {
        refund.MarkSent("rf_fixture_" + at.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), at);
        refund.MarkSettled(at);
    }

    private static void Must(bool succeeded, string what)
    {
        if (!succeeded)
            throw new InvalidOperationException($"The fixture could not build {what}.");
    }

    /// <summary>
    /// Ids and booking references are minted at random by the domain; renumbering them in order of first
    /// appearance keeps every cross-reference while making the file the same on every run.
    /// </summary>
    private static string Renumber(string json)
    {
        var ids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        json = Guids().Replace(json, match =>
        {
            if (!ids.TryGetValue(match.Value, out var renumbered))
            {
                renumbered = string.Create(CultureInfo.InvariantCulture, $"00000000-0000-4000-8000-{ids.Count + 1:D12}");
                ids[match.Value] = renumbered;
            }

            return renumbered;
        });

        var references = new Dictionary<string, string>(StringComparer.Ordinal);
        return BookingReferences().Replace(json, match =>
        {
            if (!references.TryGetValue(match.Value, out var renumbered))
            {
                renumbered = string.Create(CultureInfo.InvariantCulture, $"KH-FIXTURE{references.Count + 1}");
                references[match.Value] = renumbered;
            }

            return renumbered;
        });
    }

    private static string Difference(string committed, string generated)
    {
        var expected = generated.Split('\n');
        var actual = committed.Split('\n');
        var line = 0;
        while (line < expected.Length && line < actual.Length && string.Equals(expected[line], actual[line], StringComparison.Ordinal))
            line++;

        return $"docs/contracts/financial-documents-v1.json no longer matches what the composer writes. First difference at line {line + 1}:\n"
            + $"  committed: {(line < actual.Length ? actual[line] : "(end of file)")}\n"
            + $"  generated: {(line < expected.Length ? expected[line] : "(end of file)")}\n"
            + $"If the change is intended, regenerate with {RegenerateSwitch}=1 and review the diff; a change to the grammar is a new schema version (docs/contracts/README.md).";
    }

    [GeneratedRegex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}", RegexOptions.CultureInvariant)]
    private static partial Regex Guids();

    [GeneratedRegex(@"\bKH-[A-Z0-9]{8}\b", RegexOptions.CultureInvariant)]
    private static partial Regex BookingReferences();

    /// <summary>One document of the fixture, before its standing is known.</summary>
    private sealed record Issued(Id Id, string Number, DateTimeOffset IssuedAt, int Version, Id? PreviousId, Id Family, FinancialDocumentDraft Draft)
    {
        public FinancialDocumentRecord Record(bool voided, bool superseded) => new(
            Id,
            Draft.Type,
            Number,
            Version,
            FinancialDocumentStatus.Of(voided, superseded),
            Draft.SubjectId,
            Draft.BookingId,
            Draft.BookingReference,
            Draft.CustomerId,
            Draft.DealerId,
            Draft.PaymentId,
            Draft.RefundId,
            PreviousId,
            Draft.RelatedDocumentId,
            Draft.Cause,
            Draft.OccurredAt,
            IssuedAt,
            Draft.CoversThrough,
            Draft.CheckpointFingerprint,
            Draft.HeadlineAmount,
            Draft.Provider,
            Draft.SnapshotSchemaVersion,
            Draft.Snapshot,
            FinancialDocument.Sha256(Draft.Snapshot));
    }
}
