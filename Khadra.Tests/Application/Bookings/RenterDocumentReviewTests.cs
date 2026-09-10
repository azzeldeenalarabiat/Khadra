using Khadra.Application.Bookings.RenterDocuments;
using Khadra.Application.Common.Ports;
using Khadra.Domain.Auditing;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Domain.IdentityAccess;
using Khadra.Tests.Support;
using NSubstitute;

namespace Khadra.Tests.Application.Bookings;

/// <summary>
/// A gallery recording that it CHECKED the renter's paperwork, and the durable record of it.
/// </summary>
/// <remarks>
/// <para>
/// The wording these tests defend is "reviewed by the dealer", never "verified by Khadra". Nothing on
/// the platform verifies a document; this records that a named member of a gallery's staff opened the
/// file the renter uploaded, at a moment the server timed. Every test that asserts on what reaches
/// the client is really asserting that the platform is not making a claim it cannot support.
/// </para>
/// <para>
/// The second half is item 86: every disclosure and every review leaves a row that cannot be edited,
/// deleted or truncated, and that row carries no way back to the file.
/// </para>
/// </remarks>
public sealed class RenterDocumentReviewTests
{
    private static readonly DateTimeOffset Now = Build.Now;

    // ── Who may record a review ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_owner_records_a_review_against_their_own_live_booking()
    {
        var context = new RenterDocumentFixture();
        var booking = context.ConfirmedBooking();
        var licence = context.LicenceFront();

        var result = await context.Handlers().Handle(
            new RecordRenterDocumentReviewCommand(context.OwnerUserId, booking.Id, licence.Id), default);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.DealerReview);
        Assert.Equal(Now, result.Value.DealerReview.ReviewedAt);
        Assert.Equal(context.OwnerUserId.Value, result.Value.DealerReview.ReviewedByUserId);
        Assert.Equal("Rami Haddad", result.Value.DealerReview.ReviewedByName);
        // It lands on the BOOKING, which is what makes it this dealership's opinion rather than a
        // claim about the document itself.
        Assert.Single(booking.RenterDocumentReviews);
        await context.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_active_employee_may_record_one_and_it_carries_THEIR_name()
    {
        // Spec 4.2 puts the employee at the booking desk, and they are the person actually holding
        // the licence up against the face in front of them. The record has to say who that was.
        var context = new RenterDocumentFixture();
        var employeeUserId = context.HireEmployee();
        context.ActingAs(employeeUserId, "Layla Haddad");
        var booking = context.ConfirmedBooking();
        var licence = context.LicenceFront();

        var result = await context.Handlers().Handle(
            new RecordRenterDocumentReviewCommand(employeeUserId, booking.Id, licence.Id), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(employeeUserId.Value, result.Value.DealerReview!.ReviewedByUserId);
        Assert.Equal("Layla Haddad", result.Value.DealerReview.ReviewedByName);
    }

    [Fact]
    public async Task A_deactivated_employee_cannot_record_one()
    {
        // The only "insufficient permission" the model has: there is no separate review grant, and
        // adding one is an owner decision. Spec 4.2 ends a deactivated employee's access at once.
        var context = new RenterDocumentFixture();
        var employeeUserId = context.HireEmployee(active: false);
        context.ActingAs(employeeUserId);
        var booking = context.ConfirmedBooking();
        var licence = context.LicenceFront();

        var result = await context.Handlers().Handle(
            new RecordRenterDocumentReviewCommand(employeeUserId, booking.Id, licence.Id), default);

        Assert.True(result.IsFailure);
        Assert.Equal(DealerErrors.NotRegistered.Code, result.Error.Code);
        Assert.Empty(booking.RenterDocumentReviews);
        Assert.Empty(context.Recorded);
    }

    [Fact]
    public async Task A_different_dealership_cannot_review_another_gallerys_renter()
    {
        // The headline rule, on the write side. NOT FOUND rather than forbidden: a 403 would confirm
        // the booking id is real.
        var context = new RenterDocumentFixture();
        var strangerOwnerId = Id.New();
        var stranger = Build.ApprovedDealer(Now, strangerOwnerId, "Zarqa Auto Lease", "654321");
        context.Dealers.GetByOwnerUserIdAsync(strangerOwnerId, Arg.Any<CancellationToken>()).Returns(stranger);
        context.ActingAs(strangerOwnerId, "Zarqa Owner", UserRole.DealerOwner);
        var booking = context.ConfirmedBooking();
        var licence = context.LicenceFront();

        var result = await context.Handlers().Handle(
            new RecordRenterDocumentReviewCommand(strangerOwnerId, booking.Id, licence.Id), default);

        Assert.True(result.IsFailure);
        Assert.Equal(BookingErrors.NotFound.Code, result.Error.Code);
        Assert.Empty(booking.RenterDocumentReviews);
        Assert.Empty(context.Recorded);
    }

    [Fact]
    public async Task An_invented_booking_id_is_not_found()
    {
        var context = new RenterDocumentFixture();
        context.ConfirmedBooking();
        var licence = context.LicenceFront();

        var result = await context.Handlers().Handle(
            new RecordRenterDocumentReviewCommand(context.OwnerUserId, Id.New(), licence.Id), default);

        Assert.True(result.IsFailure);
        Assert.Equal(BookingErrors.NotFound.Code, result.Error.Code);
    }

    [Fact]
    public async Task A_document_belonging_to_another_customer_cannot_be_reviewed_onto_this_booking()
    {
        // Swapping the document id in the URL is the obvious attack on a write endpoint: it would
        // otherwise let a gallery attach a review to somebody else's paperwork.
        var context = new RenterDocumentFixture();
        var booking = context.ConfirmedBooking();
        var somebodyElse = Build.Customer(Now, email: "omar@example.jo", phone: "0797654321");
        var theirLicence = somebodyElse.Documents.First(document => document.Type.IsLicence);

        var result = await context.Handlers().Handle(
            new RecordRenterDocumentReviewCommand(context.OwnerUserId, booking.Id, theirLicence.Id), default);

        Assert.True(result.IsFailure);
        Assert.Equal(IdentityErrors.DocumentNotFound.Code, result.Error.Code);
        Assert.Empty(booking.RenterDocumentReviews);
        Assert.Empty(context.Recorded);
    }

    [Fact]
    public async Task An_invented_document_id_is_not_found()
    {
        var context = new RenterDocumentFixture();
        var booking = context.ConfirmedBooking();

        var result = await context.Handlers().Handle(
            new RecordRenterDocumentReviewCommand(context.OwnerUserId, booking.Id, Id.New()), default);

        Assert.True(result.IsFailure);
        Assert.Equal(IdentityErrors.DocumentNotFound.Code, result.Error.Code);
    }

    [Fact]
    public async Task A_suspended_dealership_may_still_record_one_on_a_car_it_is_holding()
    {
        // The same gate as viewing, for the same reason: a suspended gallery still hands the car over,
        // so it must still be able to check — and to say that it did.
        var context = new RenterDocumentFixture(trading: false);
        var booking = context.ConfirmedBooking();
        var licence = context.LicenceFront();

        var result = await context.Handlers().Handle(
            new RecordRenterDocumentReviewCommand(context.OwnerUserId, booking.Id, licence.Id), default);

        Assert.True(result.IsSuccess);
    }

    // ── For how long ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_request_inside_its_answer_window_may_be_reviewed()
    {
        // Reviewing is allowed exactly where viewing is, and NOT narrowed to the handover states. At
        // Requested the gallery is looking at the licence precisely in order to decide; refusing the
        // record then would make the record lie about when the look happened.
        var context = new RenterDocumentFixture();
        var booking = context.RequestedBooking();
        var licence = context.LicenceFront();

        var result = await context.Handlers().Handle(
            new RecordRenterDocumentReviewCommand(context.OwnerUserId, booking.Id, licence.Id), default);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task A_cancelled_booking_can_no_longer_be_reviewed()
    {
        var context = new RenterDocumentFixture();
        var booking = context.RequestedBooking();
        booking.Cancel(BookingParty.Customer, booking.CustomerId, null, Now);
        context.Given(booking);
        var licence = context.LicenceFront();

        var result = await context.Handlers().Handle(
            new RecordRenterDocumentReviewCommand(context.OwnerUserId, booking.Id, licence.Id), default);

        Assert.True(result.IsFailure);
        Assert.Equal(BookingErrors.RenterDocumentsNotAvailable.Code, result.Error.Code);
        Assert.Empty(context.Recorded);
    }

    [Fact]
    public async Task A_returned_booking_can_no_longer_be_reviewed()
    {
        var context = new RenterDocumentFixture();
        var booking = context.ConfirmedBooking();
        booking.RecordPickup(BookingParty.Dealer, context.OwnerUserId, Now);
        booking.RecordReturn(BookingParty.Dealer, context.OwnerUserId, Now.AddDays(1));
        context.Given(booking);
        var licence = context.LicenceFront();

        var result = await context.Handlers().Handle(
            new RecordRenterDocumentReviewCommand(context.OwnerUserId, booking.Id, licence.Id), default);

        Assert.True(result.IsFailure);
        Assert.Equal(BookingErrors.RenterDocumentsNotAvailable.Code, result.Error.Code);
    }

    [Fact]
    public async Task A_lapsed_answer_window_closes_the_review_action_with_the_view()
    {
        // The clock decides, not a status a job has not caught up with. The review action must
        // disappear at exactly the instant the access does, which is the owner's requirement.
        var context = new RenterDocumentFixture();
        var booking = context.RequestedBooking();
        var licence = context.LicenceFront();
        context.Clock.UtcNow = booking.DecisionDeadline.AddSeconds(1);

        var result = await context.Handlers().Handle(
            new RecordRenterDocumentReviewCommand(context.OwnerUserId, booking.Id, licence.Id), default);

        Assert.True(result.IsFailure);
        Assert.Equal(BookingErrors.RenterDocumentsNotAvailable.Code, result.Error.Code);
        // And the booking is NOT expired as a side effect of somebody trying to review it: the
        // clock's job stays the clock's.
        Assert.Equal(BookingStatus.Requested, booking.Status);
    }

    // ── Pressing it twice ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_repeat_answers_with_the_review_that_is_already_there()
    {
        // A lost response must not read as a failed click. The same reasoning as a repeated
        // cancellation: recognise the retry, answer with the current state.
        var context = new RenterDocumentFixture();
        var booking = context.ConfirmedBooking();
        var licence = context.LicenceFront();
        var first = await context.Handlers().Handle(
            new RecordRenterDocumentReviewCommand(context.OwnerUserId, booking.Id, licence.Id), default);

        context.Clock.UtcNow = Now.AddHours(3);
        var second = await context.Handlers().Handle(
            new RecordRenterDocumentReviewCommand(context.OwnerUserId, booking.Id, licence.Id), default);

        Assert.True(second.IsSuccess);
        Assert.Single(booking.RenterDocumentReviews);
        // The FIRST timestamp survives. "When did this dealership first check the licence" is the
        // whole value of the record, and a second press must not move it three hours later.
        Assert.Equal(first.Value.DealerReview!.ReviewedAt, second.Value.DealerReview!.ReviewedAt);
        Assert.Equal(Now, second.Value.DealerReview.ReviewedAt);
    }

    [Fact]
    public async Task A_colleague_pressing_it_afterwards_does_not_take_the_credit()
    {
        var context = new RenterDocumentFixture();
        var booking = context.ConfirmedBooking();
        var licence = context.LicenceFront();
        await context.Handlers().Handle(
            new RecordRenterDocumentReviewCommand(context.OwnerUserId, booking.Id, licence.Id), default);

        var employeeUserId = context.HireEmployee();
        context.ActingAs(employeeUserId, "Layla Haddad");
        var second = await context.Handlers().Handle(
            new RecordRenterDocumentReviewCommand(employeeUserId, booking.Id, licence.Id), default);

        Assert.True(second.IsSuccess);
        Assert.Equal("Rami Haddad", second.Value.DealerReview!.ReviewedByName);
        Assert.Single(booking.RenterDocumentReviews);
    }

    [Fact]
    public async Task A_repeat_writes_no_second_disclosure_record()
    {
        // Nothing was written and nothing was disclosed, so there is nothing to record. A log that
        // counted clicks would inflate the very number a customer would later be asking about.
        var context = new RenterDocumentFixture();
        var booking = context.ConfirmedBooking();
        var licence = context.LicenceFront();

        await context.Handlers().Handle(
            new RecordRenterDocumentReviewCommand(context.OwnerUserId, booking.Id, licence.Id), default);
        await context.Handlers().Handle(
            new RecordRenterDocumentReviewCommand(context.OwnerUserId, booking.Id, licence.Id), default);

        Assert.Single(context.Recorded);
        await context.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ── A replaced photograph ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Re_photographing_the_licence_clears_the_review_of_the_old_one()
    {
        // THE test this design exists for. CustomerDocument.Replace keeps the row's id and swaps the
        // file underneath it. A review keyed on the document id alone would survive that, and the
        // console would show "reviewed by dealer" over a photograph nobody at the dealership has ever
        // seen -- at the moment the car is handed over. That is item 63's failure reopened by the
        // feature meant to close it.
        var context = new RenterDocumentFixture();
        var booking = context.ConfirmedBooking();
        var licence = context.LicenceFront();
        await context.Handlers().Handle(
            new RecordRenterDocumentReviewCommand(context.OwnerUserId, booking.Id, licence.Id), default);

        // The renter sends a better photograph. Same slot, same id, new file.
        context.Renter.AttachDocument(
            CustomerDocumentType.DrivingLicenceFront,
            "customers/abc/replacement.jpg",
            "image/jpeg",
            2048,
            Now.AddHours(1));

        var listing = await context.Handlers().Handle(
            new ViewRenterDocumentsQuery(context.OwnerUserId, booking.Id), default);

        var front = listing.Value.Documents.Single(document => document.Type == "DrivingLicenceFront");
        Assert.Null(front.DealerReview);
        Assert.Equal(Now.AddHours(1), front.UploadedAt);
    }

    [Fact]
    public async Task The_new_photograph_can_be_reviewed_in_its_own_right()
    {
        var context = new RenterDocumentFixture();
        var booking = context.ConfirmedBooking();
        var licence = context.LicenceFront();
        await context.Handlers().Handle(
            new RecordRenterDocumentReviewCommand(context.OwnerUserId, booking.Id, licence.Id), default);
        context.Renter.AttachDocument(
            CustomerDocumentType.DrivingLicenceFront, "customers/abc/new.jpg", "image/jpeg", 2048, Now.AddHours(1));

        context.Clock.UtcNow = Now.AddHours(2);
        var result = await context.Handlers().Handle(
            new RecordRenterDocumentReviewCommand(context.OwnerUserId, booking.Id, licence.Id), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(Now.AddHours(2), result.Value.DealerReview!.ReviewedAt);
        // Two rows, not one edited: the old look is still on the record for the file it was about.
        Assert.Equal(2, booking.RenterDocumentReviews.Count);
    }

    // ── What the listing says about a review ────────────────────────────────────────────────────

    [Fact]
    public async Task An_unreviewed_document_carries_no_review_at_all()
    {
        var context = new RenterDocumentFixture();
        var booking = context.ConfirmedBooking();

        var listing = await context.Handlers().Handle(
            new ViewRenterDocumentsQuery(context.OwnerUserId, booking.Id), default);

        Assert.All(listing.Value.Documents, document => Assert.Null(document.DealerReview));
    }

    [Fact]
    public async Task A_review_shows_only_against_the_document_it_was_made_for()
    {
        var context = new RenterDocumentFixture();
        var booking = context.ConfirmedBooking();
        var licence = context.LicenceFront();
        await context.Handlers().Handle(
            new RecordRenterDocumentReviewCommand(context.OwnerUserId, booking.Id, licence.Id), default);

        var listing = await context.Handlers().Handle(
            new ViewRenterDocumentsQuery(context.OwnerUserId, booking.Id), default);

        var reviewed = listing.Value.Documents.Where(document => document.DealerReview is not null).ToList();
        Assert.Single(reviewed);
        Assert.Equal("DrivingLicenceFront", reviewed[0].Type);
    }

    [Fact]
    public async Task Another_dealerships_review_is_invisible_here()
    {
        // The review belongs to a BOOKING. Two galleries renting to the same customer each check the
        // licence for themselves, and neither one's look says anything about the other's.
        var context = new RenterDocumentFixture();
        var theirs = context.ConfirmedBooking();
        var licence = context.LicenceFront();
        await context.Handlers().Handle(
            new RecordRenterDocumentReviewCommand(context.OwnerUserId, theirs.Id, licence.Id), default);

        // A second booking, same renter, same gallery object but a different booking: the review does
        // not carry across, because it was never a fact about the document.
        var another = context.Given(Build.ConfirmedBooking(
            Now, customerId: context.Renter.Id, dealerId: context.Dealer.Id));
        var listing = await context.Handlers().Handle(
            new ViewRenterDocumentsQuery(context.OwnerUserId, another.Id), default);

        Assert.All(listing.Value.Documents, document => Assert.Null(document.DealerReview));
    }

    [Fact]
    public async Task The_platform_review_status_never_reaches_the_gallery()
    {
        // CustomerDocument.Status can take the value "Verified", and beside a dealer's own review it
        // would read as a Khadra guarantee. It is not on this shape and must not come back.
        var context = new RenterDocumentFixture();
        var booking = context.ConfirmedBooking();

        var listing = await context.Handlers().Handle(
            new ViewRenterDocumentsQuery(context.OwnerUserId, booking.Id), default);

        var rendered = System.Text.Json.JsonSerializer.Serialize(listing.Value);
        Assert.DoesNotContain("PendingReview", rendered, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Verified", rendered, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"status\"", rendered, StringComparison.OrdinalIgnoreCase);
    }

    // ── The durable record (item 86) ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Opening_a_document_writes_a_Viewed_record_before_the_bytes_go_out()
    {
        var context = new RenterDocumentFixture();
        var booking = context.ConfirmedBooking();
        var licence = context.LicenceFront();

        var result = await context.Handlers().Handle(
            new OpenRenterDocumentCommand(context.OwnerUserId, booking.Id, licence.Id), default);

        Assert.True(result.IsSuccess);
        var entry = Assert.Single(context.Recorded);
        Assert.Equal(DocumentAccessAction.Viewed, entry.Action);
        Assert.Equal(context.OwnerUserId, entry.ActorUserId);
        Assert.Equal("Rami Haddad", entry.ActorName);
        Assert.Equal(context.Dealer.Id, entry.DealerId);
        Assert.Equal(booking.Id, entry.BookingId);
        Assert.Equal(context.Renter.Id, entry.SubjectUserId);
        Assert.Equal(licence.Id, entry.DocumentId);
        Assert.Equal(CustomerDocumentType.DrivingLicenceFront, entry.DocumentType);
        Assert.Equal(licence.UploadedAt, entry.DocumentUploadedAt);
        // Committed BEFORE the caller is handed the stream.
        await context.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Recording_a_review_writes_a_Reviewed_record_in_the_same_save()
    {
        var context = new RenterDocumentFixture();
        var booking = context.ConfirmedBooking();
        var licence = context.LicenceFront();

        await context.Handlers().Handle(
            new RecordRenterDocumentReviewCommand(context.OwnerUserId, booking.Id, licence.Id), default);

        var entry = Assert.Single(context.Recorded);
        Assert.Equal(DocumentAccessAction.Reviewed, entry.Action);
        Assert.Equal(booking.Id, entry.BookingId);
        Assert.Equal(context.Renter.Id, entry.SubjectUserId);
        Assert.Equal(licence.UploadedAt, entry.DocumentUploadedAt);
        // ONE save: the review row and its record commit together or not at all, which is the rule
        // CLAUDE.md states for the audit trail.
        await context.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Every_view_is_recorded_separately_rather_than_collapsed()
    {
        // No de-duplication, deliberately. Repetition is itself evidence: an employee opening a
        // passport forty times is exactly what a disclosure log exists to surface, and collapsing
        // rows at write time cannot be undone later.
        var context = new RenterDocumentFixture();
        var booking = context.ConfirmedBooking();
        var licence = context.LicenceFront();

        for (var attempt = 0; attempt < 3; attempt++)
        {
            await context.Handlers().Handle(
                new OpenRenterDocumentCommand(context.OwnerUserId, booking.Id, licence.Id), default);
        }

        Assert.Equal(3, context.Recorded.Count);
    }

    [Fact]
    public async Task A_view_that_cannot_be_recorded_is_not_served()
    {
        // "No record, no disclosure." The audit INSERT goes to the same database on the same
        // connection as the reads that just authorised the request, so the window in which it can fail
        // alone is tiny -- and serving anyway would make "the log shows nobody looked" stop meaning
        // anything during exactly the incident somebody would later be investigating.
        var context = new RenterDocumentFixture();
        var booking = context.ConfirmedBooking();
        var licence = context.LicenceFront();
        context.UnitOfWork
            .SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns<Task<int>>(_ => throw new InvalidOperationException("the log is unreachable"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => context.Handlers().Handle(
            new OpenRenterDocumentCommand(context.OwnerUserId, booking.Id, licence.Id), default));
    }

    [Fact]
    public async Task A_view_that_cannot_be_recorded_does_not_leak_the_storage_stream()
    {
        // The handle is ours until the caller has it. Letting the exception escape without disposing
        // would leak a storage connection on every failed write.
        var context = new RenterDocumentFixture();
        var booking = context.ConfirmedBooking();
        var licence = context.LicenceFront();
        var stream = new TrackingStream();
        var storage = Substitute.For<IDocumentStorage>();
        storage.OpenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns<Stream?>(stream);
        context.UnitOfWork
            .SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns<Task<int>>(_ => throw new InvalidOperationException("the log is unreachable"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => context.Handlers(storage).Handle(
            new OpenRenterDocumentCommand(context.OwnerUserId, booking.Id, licence.Id), default));

        Assert.True(stream.WasDisposed);
    }

    [Fact]
    public async Task A_refused_view_records_nothing()
    {
        var context = new RenterDocumentFixture();
        var strangerOwnerId = Id.New();
        var stranger = Build.ApprovedDealer(Now, strangerOwnerId, "Zarqa Auto Lease", "654321");
        context.Dealers.GetByOwnerUserIdAsync(strangerOwnerId, Arg.Any<CancellationToken>()).Returns(stranger);
        context.ActingAs(strangerOwnerId, "Zarqa Owner", UserRole.DealerOwner);
        var booking = context.ConfirmedBooking();
        var licence = context.LicenceFront();

        var result = await context.Handlers().Handle(
            new OpenRenterDocumentCommand(strangerOwnerId, booking.Id, licence.Id), default);

        Assert.True(result.IsFailure);
        Assert.Empty(context.Recorded);
    }

    [Fact]
    public async Task Listing_the_documents_records_nothing()
    {
        // Deliberate, and written down in item 86 rather than overclaimed. The listing fires on every
        // booking-detail open with nobody pressing anything, so logging it would fill the record with
        // "views" the gallery never chose. It describes what exists; it reveals nothing.
        var context = new RenterDocumentFixture();
        var booking = context.ConfirmedBooking();

        await context.Handlers().Handle(
            new ViewRenterDocumentsQuery(context.OwnerUserId, booking.Id), default);

        Assert.Empty(context.Recorded);
    }

    [Fact]
    public async Task A_disclosure_record_carries_no_way_back_to_the_file()
    {
        // The one thing this table must never hold. A log that carried the storage key would be a
        // second route into the very documents it exists to protect.
        var context = new RenterDocumentFixture();
        var booking = context.ConfirmedBooking();
        var licence = context.LicenceFront();

        await context.Handlers().Handle(
            new OpenRenterDocumentCommand(context.OwnerUserId, booking.Id, licence.Id), default);

        var rendered = System.Text.Json.JsonSerializer.Serialize(context.Recorded);
        Assert.DoesNotContain(licence.StorageKey, rendered, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("customers/", rendered, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("supabase", rendered, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("http", rendered, StringComparison.OrdinalIgnoreCase);
        // And no contact details for the renter either: an id, so a name can never be wrong or stale
        // in a table nothing can edit.
        Assert.DoesNotContain(context.Renter.Email.Value, rendered, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A stream that says whether anybody closed it.</summary>
    private sealed class TrackingStream : MemoryStream
    {
        public bool WasDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            base.Dispose(disposing);
        }

        public override ValueTask DisposeAsync()
        {
            WasDisposed = true;
            return base.DisposeAsync();
        }
    }
}
