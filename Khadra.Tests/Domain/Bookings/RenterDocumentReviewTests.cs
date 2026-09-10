using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Tests.Support;

namespace Khadra.Tests.Domain.Bookings;

/// <summary>
/// The rule itself, on the aggregate, with no doubles in the way.
/// </summary>
/// <remarks>
/// The handler asks <c>FindRenterDocumentReview</c> first and answers a repeat with the record that
/// already exists, so these are the invariants underneath that convenience: the aggregate stays
/// strict, and it refuses on its own account rather than trusting a caller to have checked.
/// </remarks>
public sealed class RenterDocumentReviewTests
{
    private static readonly DateTimeOffset Now = Build.Now;
    private static readonly Id Reviewer = Id.New();

    private static Result Record(
        Booking booking,
        Id documentId,
        DateTimeOffset uploadedAt,
        DateTimeOffset? at = null) =>
        new(booking.RecordRenterDocumentReview(
            documentId,
            CustomerDocumentType.DrivingLicenceFront,
            uploadedAt,
            Reviewer,
            "Rami Haddad",
            at ?? Now));

    private readonly record struct Result(
        CSharpFunctionalExtensions.Result<RenterDocumentReview, Error> Value)
    {
        public bool IsSuccess => Value.IsSuccess;
        public Error Error => Value.Error;
        public RenterDocumentReview Review => Value.Value;
    }

    [Fact]
    public void A_live_booking_takes_a_review_and_records_who_made_it_and_when()
    {
        var booking = Build.ConfirmedBooking(Now);
        var documentId = Id.New();

        var result = Record(booking, documentId, Now.AddDays(-1));

        Assert.True(result.IsSuccess);
        Assert.Equal(Reviewer, result.Review.ReviewedByUserId);
        Assert.Equal("Rami Haddad", result.Review.ReviewedByName);
        Assert.Equal(Now, result.Review.ReviewedAt);
        Assert.Equal(booking.Id, result.Review.BookingId);
        Assert.Single(booking.RenterDocumentReviews);
    }

    [Fact]
    public void A_booking_that_is_no_longer_live_refuses_the_review()
    {
        // The same predicate that decides whether the gallery may SEE the document. Stated here as
        // well as in the handler so the two cannot drift: recording a check of something you can no
        // longer open would be writing a claim you cannot support.
        var booking = Build.ConfirmedBooking(Now);
        booking.RecordPickup(BookingParty.Dealer, Reviewer, Now);
        booking.RecordReturn(BookingParty.Dealer, Reviewer, Now.AddDays(1));

        var result = Record(booking, Id.New(), Now.AddDays(-1), Now.AddDays(2));

        Assert.False(result.IsSuccess);
        Assert.Equal(BookingErrors.RenterDocumentsNotAvailable.Code, result.Error.Code);
        Assert.Empty(booking.RenterDocumentReviews);
    }

    [Fact]
    public void A_lapsed_answer_window_refuses_it_without_expiring_the_booking()
    {
        // The clock has already decided; only the status is behind. A gallery reviewing a licence must
        // not expire a booking as a side effect of looking at it.
        var booking = Build.Booking(Now);

        var result = Record(booking, Id.New(), Now.AddDays(-1), booking.DecisionDeadline.AddSeconds(1));

        Assert.False(result.IsSuccess);
        Assert.Equal(BookingErrors.RenterDocumentsNotAvailable.Code, result.Error.Code);
        Assert.Equal(BookingStatus.Requested, booking.Status);
    }

    [Fact]
    public void The_aggregate_refuses_a_second_review_of_the_same_upload()
    {
        // Strict on purpose. The handler recognises the retry and answers with what is already there;
        // this is what stops any other caller moving the timestamp, and "when did this dealership
        // first check the licence" is the whole value of the record.
        var booking = Build.ConfirmedBooking(Now);
        var documentId = Id.New();
        var uploadedAt = Now.AddDays(-1);
        Record(booking, documentId, uploadedAt);

        var second = Record(booking, documentId, uploadedAt, Now.AddHours(5));

        Assert.False(second.IsSuccess);
        Assert.Equal(BookingErrors.RenterDocumentAlreadyReviewed.Code, second.Error.Code);
        Assert.Equal(ErrorKind.Conflict, second.Error.Kind);
        Assert.Single(booking.RenterDocumentReviews);
        Assert.Equal(Now, booking.RenterDocumentReviews.Single().ReviewedAt);
    }

    [Fact]
    public void A_replaced_upload_is_a_different_thing_to_review()
    {
        // CustomerDocument.Replace keeps the row id and swaps the file, so the upload instant is what
        // identifies WHAT was reviewed. Without this, a re-photographed licence would inherit a review
        // nobody made.
        var booking = Build.ConfirmedBooking(Now);
        var documentId = Id.New();
        Record(booking, documentId, Now.AddDays(-1));

        var afterReupload = Record(booking, documentId, Now.AddMinutes(-5), Now.AddMinutes(1));

        Assert.True(afterReupload.IsSuccess);
        Assert.Equal(2, booking.RenterDocumentReviews.Count);
    }

    [Fact]
    public void A_review_is_found_only_for_the_exact_upload_it_was_made_for()
    {
        var booking = Build.ConfirmedBooking(Now);
        var documentId = Id.New();
        var uploadedAt = Now.AddDays(-1);
        Record(booking, documentId, uploadedAt);

        Assert.NotNull(booking.FindRenterDocumentReview(documentId, uploadedAt));
        // One second later is a different file.
        Assert.Null(booking.FindRenterDocumentReview(documentId, uploadedAt.AddSeconds(1)));
        Assert.Null(booking.FindRenterDocumentReview(Id.New(), uploadedAt));
    }

    [Fact]
    public void A_review_requires_a_reviewer_and_a_name()
    {
        // Programming errors, not user errors: nothing on the wire can produce these, because the
        // reviewer comes from the validated token.
        var booking = Build.ConfirmedBooking(Now);

        Assert.Throws<DomainException>(() => booking.RecordRenterDocumentReview(
            Id.New(), CustomerDocumentType.Passport, Now, Id.Empty, "Rami", Now));
        Assert.Throws<DomainException>(() => booking.RecordRenterDocumentReview(
            Id.New(), CustomerDocumentType.Passport, Now, Reviewer, "   ", Now));
    }

    [Fact]
    public void Reviews_of_different_documents_sit_side_by_side()
    {
        var booking = Build.ConfirmedBooking(Now);
        var uploadedAt = Now.AddDays(-1);

        Record(booking, Id.New(), uploadedAt);
        Record(booking, Id.New(), uploadedAt);
        Record(booking, Id.New(), uploadedAt);

        Assert.Equal(3, booking.RenterDocumentReviews.Count);
    }
}
