using Khadra.Application.Common.Ports;
using Khadra.Application.Dealers.GetDealerForReview;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Domain.Dealers.Repositories;
using Khadra.Tests.Support;
using NSubstitute;

namespace Khadra.Tests.Application.Dealers;

// What the Admin's review screen is handed before they make the spec 3.1 decision.
//
// These assertions exist because the screen used to fill the same slots from constants written into
// the console: "of 3" documents, "the 48-hour review SLA", and a "{type}.jpg" filename for files
// that are in fact PDFs. Each of those was right only by coincidence, and each is now the server's
// answer -- so each is pinned here.
public sealed class DealerReviewScreenTests
{
    private static readonly System.Text.Json.JsonSerializerOptions WireOptions =
        new(System.Text.Json.JsonSerializerDefaults.Web);

    private sealed class Context
    {
        public IDealerRepository Dealers { get; } = Substitute.For<IDealerRepository>();
        public IDocumentLinkSigner Signer { get; } = Substitute.For<IDocumentLinkSigner>();
        public TestClock Clock { get; } = new(Build.Now);

        public Context()
        {
            Signer
                .Sign(Arg.Any<string>(), Arg.Any<DateTimeOffset>())
                .Returns(call => new SignedDocumentLink(
                    $"/api/v1/documents/{call.ArgAt<string>(0)}",
                    call.ArgAt<DateTimeOffset>(1).AddMinutes(5)));
        }

        public Dealer Given(Dealer dealer)
        {
            Dealers.GetByIdAsync(dealer.Id, Arg.Any<CancellationToken>()).Returns(dealer);
            return dealer;
        }

        public GetDealerForReviewHandler Handler() => new(Dealers, Signer, Clock);

        public Task<CSharpFunctionalExtensions.Result<DealerReviewDto, Error>> Load(Dealer dealer) =>
            Handler().Handle(new GetDealerForReviewQuery(dealer.Id), CancellationToken.None);
    }

    private static Dealer PendingWithDocuments()
    {
        var dealer = Build.Dealer();
        Build.AttachAllDocuments(dealer);
        return dealer;
    }

    [Fact]
    public async Task The_screen_is_told_which_documents_an_approval_requires()
    {
        var context = new Context();
        var dealer = context.Given(PendingWithDocuments());

        var result = await context.Load(dealer);

        Assert.True(result.IsSuccess);
        // The console counts this list rather than printing "of 3", so a fourth required type shows
        // up on screen without a frontend release -- and cannot silently fail to.
        Assert.Equal(
            DealerDocumentType.Required.Select(type => type.Name),
            result.Value.Dealer.RequiredDocuments);
    }

    [Fact]
    public async Task An_incomplete_application_names_what_is_still_missing_against_the_full_list()
    {
        var context = new Context();
        var dealer = Build.Dealer();
        dealer.AttachDocument(DealerDocumentType.CommercialRegistration, "dealers/a/one.pdf", Build.Now);
        context.Given(dealer);

        var result = await context.Load(dealer);

        Assert.True(result.IsSuccess);
        Assert.Equal(DealerDocumentType.Required.Count, result.Value.Dealer.RequiredDocuments.Count);
        Assert.Single(result.Value.Dealer.SubmittedDocuments);
        Assert.Equal(
            DealerDocumentType.Required.Count - 1,
            result.Value.Dealer.MissingDocuments.Count);
    }

    // The tile says what the reviewer is about to open. It said ".jpg" for everything, including the
    // PDFs the seeder writes, on the one screen whose entire purpose is looking at the documents.
    [Theory]
    [InlineData("dealers/a/registration.pdf", "application/pdf")]
    [InlineData("dealers/a/registration.png", "image/png")]
    [InlineData("dealers/a/registration.webp", "image/webp")]
    [InlineData("dealers/a/registration.jpg", "image/jpeg")]
    public async Task A_document_is_labelled_with_the_type_it_will_actually_be_served_as(
        string storageKey,
        string expected)
    {
        var context = new Context();
        var dealer = Build.Dealer();
        dealer.AttachDocument(DealerDocumentType.CommercialRegistration, storageKey, Build.Now);
        context.Given(dealer);

        var result = await context.Load(dealer);

        Assert.True(result.IsSuccess);
        var document = Assert.Single(result.Value.Documents);
        Assert.Equal(expected, document.ContentType);
    }

    [Fact]
    public async Task Documents_come_back_in_a_stable_order_with_a_link_that_expires()
    {
        var context = new Context();
        var dealer = context.Given(PendingWithDocuments());

        var result = await context.Load(dealer);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            DealerDocumentType.Required.OrderBy(type => type.Id).Select(type => type.Name),
            result.Value.Documents.Select(document => document.Type));
        Assert.All(result.Value.Documents, document => Assert.True(document.ExpiresAt > Build.Now));
    }

    /// <summary>
    /// The window the console reads its "48-hour review SLA" wording out of. It is the frozen one on
    /// the application, not the setting in force today, which is why the handler takes no
    /// IBusinessRulesProvider: lowering the SLA must not re-judge an application already in flight.
    /// </summary>
    [Fact]
    public async Task The_promised_window_is_the_one_frozen_on_the_application()
    {
        var context = new Context();
        var dealer = context.Given(PendingWithDocuments());

        var result = await context.Load(dealer);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            Build.ReviewSla,
            result.Value.Dealer.ReviewDueAt - result.Value.Dealer.SubmittedAt);
    }

    [Fact]
    public async Task An_application_past_its_frozen_deadline_is_reported_as_breaching()
    {
        var context = new Context();
        var dealer = context.Given(PendingWithDocuments());

        var before = await context.Load(dealer);
        Assert.False(before.Value.IsBreachingSla);

        context.Clock.Advance(Build.ReviewSla + TimeSpan.FromHours(1));
        var after = await context.Load(dealer);

        Assert.True(after.Value.IsBreachingSla);
    }

    // The timeline carries instants; the console renders them in the reader's own zone. This entry
    // used to spell the same moment out again as a UTC string in its detail, so one row showed one
    // deadline twice, in two zones, three words apart. It now carries no words at all.
    [Fact]
    public async Task The_pending_timeline_entry_is_the_frozen_deadline_and_nothing_else()
    {
        var context = new Context();
        var dealer = context.Given(PendingWithDocuments());

        var result = await context.Load(dealer);

        var awaiting = Assert.Single(
            result.Value.Timeline,
            entry => entry.Step == DealerReviewTimelineSteps.AwaitingDecision);
        Assert.Equal(dealer.ReviewDueAt, awaiting.OccurredAt);
        Assert.False(awaiting.IsComplete);
        Assert.Null(awaiting.Decision);
        Assert.Null(awaiting.Note);
        Assert.Null(awaiting.DocumentCount);
    }

    /// <summary>
    /// The timeline is facts for the console to word, never English for it to print.
    ///
    /// Every step used to arrive as a label and a detail written here in English, which an Arabic
    /// screen showed as it came. The document count is the server's, beside the number an approval
    /// needs, so the console never writes "of 3".
    /// </summary>
    [Fact]
    public async Task The_timeline_sends_facts_rather_than_sentences()
    {
        var context = new Context();
        var dealer = Build.Dealer();
        dealer.AttachDocument(DealerDocumentType.CommercialRegistration, "dealers/a/one.pdf", Build.Now.AddMinutes(5));
        context.Given(dealer);

        var result = await context.Load(dealer);

        Assert.Equal(
            [DealerReviewTimelineSteps.Submitted, DealerReviewTimelineSteps.DocumentsAttached, DealerReviewTimelineSteps.AwaitingDecision],
            result.Value.Timeline.Select(entry => entry.Step));

        var submitted = result.Value.Timeline[0];
        Assert.Equal(dealer.SubmittedAt, submitted.OccurredAt);
        Assert.True(submitted.IsComplete);

        var documents = result.Value.Timeline[1];
        Assert.Equal(1, documents.DocumentCount);
        Assert.Equal(DealerDocumentType.Required.Count, documents.RequiredDocumentCount);
        Assert.Equal(Build.Now.AddMinutes(5), documents.OccurredAt);
        Assert.False(documents.IsComplete);

        using var json = System.Text.Json.JsonDocument.Parse(
            System.Text.Json.JsonSerializer.Serialize(result.Value.Timeline[1], WireOptions));
        var names = json.RootElement.EnumerateObject().Select(property => property.Name).ToArray();
        Assert.Equal(
            ["step", "decision", "note", "documentCount", "requiredDocumentCount", "occurredAt", "isComplete"],
            names);
    }

    /// <summary>
    /// A resubmitted application is waiting on an Admin again, and its timeline has to say so.
    ///
    /// Dealer.Resubmit restarts the clock but deliberately keeps ReviewedAt: the earlier decision
    /// really did happen. The timeline branched on ReviewedAt alone, so the one application whose
    /// deadline had just been reset was the one whose timeline announced a completed decision --
    /// labelled with the raw status name, dated at the old review -- and omitted the new deadline
    /// entirely.
    /// </summary>
    [Fact]
    public async Task A_resubmitted_application_is_shown_as_awaiting_a_decision_again()
    {
        var context = new Context();
        var dealer = PendingWithDocuments();
        dealer.RequestClarification(Id.New(), "The registration photo is unreadable.", Build.Now);
        dealer.Resubmit(Build.Now.AddHours(3), Build.ReviewSla);
        context.Given(dealer);

        var result = await context.Load(dealer);

        Assert.True(result.IsSuccess);
        var awaiting = Assert.Single(
            result.Value.Timeline,
            entry => entry.Step == DealerReviewTimelineSteps.AwaitingDecision);
        Assert.Equal(dealer.ReviewDueAt, awaiting.OccurredAt);
        Assert.False(awaiting.IsComplete);

        // The earlier decision stays on the record -- it happened -- followed by the resubmission that
        // answered it. It is never named after the CURRENT status, which would read as though the
        // decision were still pending.
        var decision = Assert.Single(result.Value.Timeline, entry => entry.Step == DealerReviewTimelineSteps.Decision);
        Assert.NotEqual(nameof(DealerVerificationStatus.PendingReview), decision.Decision);
        Assert.Contains(result.Value.Timeline, entry => entry.Step == DealerReviewTimelineSteps.Resubmitted);
    }

    /// <summary>
    /// Once the dealer has resubmitted, nothing on the aggregate says which decision came before.
    ///
    /// A clarification request and a rejection can both be answered by resubmitting, and both leave
    /// the application PendingReview with the note cleared. The timeline used to call either one
    /// "Clarification requested" -- wrong for a rejection that came back. It now says it cannot tell.
    /// </summary>
    [Fact]
    public async Task A_decision_the_dealer_has_answered_is_not_guessed_at()
    {
        foreach (var decide in new Action<Dealer>[]
                 {
                     dealer => dealer.RequestClarification(Id.New(), "The registration photo is unreadable.", Build.Now),
                     dealer => dealer.Reject(Id.New(), "The commercial registration does not match.", Build.Now),
                 })
        {
            var context = new Context();
            var dealer = PendingWithDocuments();
            decide(dealer);
            Assert.True(dealer.Resubmit(Build.Now.AddHours(3), Build.ReviewSla).IsSuccess);
            context.Given(dealer);

            var result = await context.Load(dealer);

            var decision = Assert.Single(result.Value.Timeline, entry => entry.Step == DealerReviewTimelineSteps.Decision);
            Assert.Null(decision.Decision);
            Assert.Equal(Build.Now, decision.OccurredAt);
        }
    }

    [Fact]
    public async Task A_settled_application_reads_as_settled_rather_than_still_waiting()
    {
        var context = new Context();
        var dealer = PendingWithDocuments();
        dealer.Reject(Id.New(), "The commercial registration does not match the applicant.", Build.Now);
        context.Given(dealer);

        var result = await context.Load(dealer);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain(
            result.Value.Timeline,
            entry => entry.Step == DealerReviewTimelineSteps.AwaitingDecision);
        var decision = Assert.Single(result.Value.Timeline, entry => entry.Step == DealerReviewTimelineSteps.Decision);
        Assert.Equal(nameof(DealerVerificationStatus.Rejected), decision.Decision);
        // The reviewer's own words, exactly as they typed them.
        Assert.Equal("The commercial registration does not match the applicant.", decision.Note);
    }

    /// <summary>A decision nobody has answered yet is named; an approval carries no note, and none is invented.</summary>
    [Fact]
    public async Task A_decision_still_standing_is_named_and_carries_only_the_note_on_record()
    {
        var clarifying = PendingWithDocuments();
        clarifying.RequestClarification(Id.New(), "The registration photo is unreadable.", Build.Now);
        var approved = PendingWithDocuments();
        approved.Approve(Id.New(), Build.Now);

        var context = new Context();
        context.Given(clarifying);
        context.Given(approved);

        var asked = Assert.Single((await context.Load(clarifying)).Value.Timeline, entry => entry.Step == DealerReviewTimelineSteps.Decision);
        Assert.Equal(nameof(DealerVerificationStatus.ClarificationNeeded), asked.Decision);
        Assert.Equal("The registration photo is unreadable.", asked.Note);

        var yes = Assert.Single((await context.Load(approved)).Value.Timeline, entry => entry.Step == DealerReviewTimelineSteps.Decision);
        Assert.Equal(nameof(DealerVerificationStatus.Approved), yes.Decision);
        Assert.Null(yes.Note);
    }

    [Fact]
    public async Task An_unknown_dealer_is_not_found()
    {
        var context = new Context();

        var result = await context.Handler()
            .Handle(new GetDealerForReviewQuery(Id.New()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(DealerErrors.NotRegistered.Code, result.Error.Code);
    }
}
