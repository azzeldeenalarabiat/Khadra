using Khadra.Domain.Auditing;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Tests.Support;

namespace Khadra.Tests.Domain.IdentityAccess;

/// <summary>
/// An administrator may reject a renter's document (Wave 4, W4-9; checklist 27, owner D3): a rejected file counts as not
/// filed, so the customer must upload a new one before their next request.
/// </summary>
public sealed class DocumentRejectionTests
{
    private static readonly DateTimeOffset Now = Users.Now;

    private static User Complete(bool foreign = false)
    {
        var user = foreign
            ? User.RegisterCustomer(
                EmailAddress.Create("sam@example.com").Value,
                PhoneNumber.Create("0791234500").Value,
                PersonName.Create("Sam Lee").Value,
                PasswordHash.FromHash("hashed:x"),
                Now,
                Users.AdultBirthDate,
                isForeignNational: true)
            : Users.Customer();
        user.AttachDocument(CustomerDocumentType.DrivingLicenceFront, "customers/a/1.jpg", "image/jpeg", 10, Now);
        user.AttachDocument(CustomerDocumentType.DrivingLicenceBack, "customers/a/2.jpg", "image/jpeg", 10, Now);
        user.AttachDocument(foreign ? CustomerDocumentType.Passport : CustomerDocumentType.NationalId, "customers/a/3.jpg", "image/jpeg", 10, Now);
        return user;
    }

    private static CustomerDocument Of(User user, CustomerDocumentType type) =>
        user.Documents.Single(document => document.Type == type);

    [Fact]
    public void Rejecting_the_file_that_was_opened_records_why_and_it_no_longer_counts_as_filed()
    {
        var user = Complete();
        var front = Of(user, CustomerDocumentType.DrivingLicenceFront);

        var rejected = user.RejectDocument(front.Id, "  The photo is too blurred to read.  ", front.UploadedAt);

        Assert.True(rejected.IsSuccess);
        Assert.Same(CustomerDocumentStatus.Rejected, front.Status);
        Assert.Equal("The photo is too blurred to read.", front.ReviewNote);
        Assert.False(user.HasCompleteRenterDocuments);
        Assert.Equal(["DrivingLicenceFront"], user.MissingRenterDocumentTypes().Select(type => type.Name));
        Assert.Equal(["DrivingLicenceFront"], user.RejectedRenterDocumentTypes().Select(type => type.Name));
    }

    /// <summary>A document row is a slot whose file is replaced in place: a different upload is not what was judged.</summary>
    [Fact]
    public void A_file_replaced_since_it_was_opened_is_not_rejected()
    {
        var user = Complete();
        var front = Of(user, CustomerDocumentType.DrivingLicenceFront);
        var viewed = front.UploadedAt;
        user.AttachDocument(CustomerDocumentType.DrivingLicenceFront, "customers/a/4.jpg", "image/jpeg", 10, Now.AddMinutes(5));

        var rejected = user.RejectDocument(front.Id, "Blurred.", viewed);

        Assert.Equal(IdentityErrors.DocumentChangedSinceViewed, rejected.Error);
        Assert.Same(CustomerDocumentStatus.PendingReview, front.Status);
    }

    [Fact]
    public void A_document_that_is_not_this_persons_is_not_found()
    {
        var user = Complete();

        Assert.Equal(IdentityErrors.DocumentNotFound, user.RejectDocument(Id.New(), "Blurred.", Now).Error);
    }

    [Fact]
    public void A_rejected_file_may_be_rejected_again_with_a_new_reason()
    {
        var user = Complete();
        var front = Of(user, CustomerDocumentType.DrivingLicenceFront);
        user.RejectDocument(front.Id, "Blurred.", front.UploadedAt);

        var again = user.RejectDocument(front.Id, "Expired licence.", front.UploadedAt);

        Assert.True(again.IsSuccess);
        Assert.Equal("Expired licence.", front.ReviewNote);
    }

    [Fact]
    public void A_new_upload_after_a_rejection_starts_again_and_fills_the_slot()
    {
        var user = Complete();
        var front = Of(user, CustomerDocumentType.DrivingLicenceFront);
        user.RejectDocument(front.Id, "Blurred.", front.UploadedAt);

        user.AttachDocument(CustomerDocumentType.DrivingLicenceFront, "customers/a/5.jpg", "image/jpeg", 10, Now.AddHours(1));

        Assert.Same(CustomerDocumentStatus.PendingReview, front.Status);
        Assert.Null(front.ReviewNote);
        Assert.True(user.HasCompleteRenterDocuments);
        Assert.Empty(user.RejectedRenterDocumentTypes());
    }

    /// <summary>One requirement, two answers: a rejected national ID still leaves the slot filled by an accepted passport.</summary>
    [Fact]
    public void A_rejected_identity_document_names_the_slot_only_while_nothing_else_fills_it()
    {
        var user = Complete();
        var id = Of(user, CustomerDocumentType.NationalId);
        user.RejectDocument(id.Id, "Not legible.", id.UploadedAt);

        Assert.Equal(["NationalId"], user.MissingRenterDocumentTypes().Select(type => type.Name));
        Assert.Equal(["NationalId"], user.RejectedRenterDocumentTypes().Select(type => type.Name));

        user.AttachDocument(CustomerDocumentType.Passport, "customers/a/6.jpg", "image/jpeg", 10, Now.AddHours(1));

        Assert.True(user.HasCompleteRenterDocuments);
        Assert.Empty(user.RejectedRenterDocumentTypes());
    }

    [Fact]
    public void A_rejection_says_why_within_the_width_of_its_column()
    {
        var user = Complete();
        var front = Of(user, CustomerDocumentType.DrivingLicenceFront);

        Assert.ThrowsAny<ArgumentException>(() => user.RejectDocument(front.Id, "   ", front.UploadedAt));
        Assert.Throws<DomainException>(() => user.RejectDocument(front.Id, new string('x', CustomerDocument.MaxReviewNoteLength + 1), front.UploadedAt));
    }

    [Fact]
    public void An_administrators_view_is_recorded_with_no_dealership_and_no_booking()
    {
        var subject = Id.New();
        var document = Id.New();

        var entry = DocumentAccessEntry.RecordAdminView(Id.New(), "Dana Saleh", subject, document, CustomerDocumentType.Passport, Now, Now.AddMinutes(1));

        Assert.Null(entry.DealerId);
        Assert.Null(entry.BookingId);
        Assert.Same(UserRole.Admin, entry.ActorRole);
        Assert.Same(DocumentAccessAction.Viewed, entry.Action);
        Assert.Equal(subject, entry.SubjectUserId);
        Assert.Equal(document, entry.DocumentId);
        Assert.Equal(Now, entry.DocumentUploadedAt);
    }

    [Fact]
    public void An_offices_view_still_names_the_booking_that_granted_it()
    {
        Assert.Throws<DomainException>(() => DocumentAccessEntry.Record(
            DocumentAccessAction.Viewed, Id.New(), "Omar", UserRole.DealerOwner, Id.New(), Id.Empty, Id.New(), Id.New(),
            CustomerDocumentType.Passport, Now, Now));
    }
}
