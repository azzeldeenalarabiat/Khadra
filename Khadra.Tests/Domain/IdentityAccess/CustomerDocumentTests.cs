using Khadra.Domain.IdentityAccess;
using Khadra.Tests.Support;

namespace Khadra.Tests.Domain.IdentityAccess;

public sealed class CustomerDocumentTests
{
    private static readonly DateTimeOffset Now = Users.Now;

    private static User Customer() => Users.Customer();

    [Fact]
    public void A_new_customer_holds_no_documents_and_is_not_ready_to_book()
    {
        var user = Customer();

        Assert.Empty(user.Documents);
        Assert.False(user.HasCompleteRenterDocuments);
    }

    [Fact]
    public void Attaching_a_document_records_the_storage_key_and_leaves_it_pending_review()
    {
        var user = Customer();

        var superseded = user.AttachDocument(
            CustomerDocumentType.DrivingLicenceFront, "customers/abc/1.jpg", "image/jpeg", 1024, Now);

        Assert.Null(superseded);
        var document = Assert.Single(user.Documents);
        Assert.Same(CustomerDocumentType.DrivingLicenceFront, document.Type);
        Assert.Same(CustomerDocumentStatus.PendingReview, document.Status);
        Assert.Equal("customers/abc/1.jpg", document.StorageKey);
        Assert.Equal(1024, document.SizeBytes);
    }

    [Fact]
    public void Re_uploading_a_type_replaces_it_and_names_the_file_to_delete()
    {
        // Spec 5.1: a customer told their photo is unreadable sends a better one. They should end up
        // with one licence front, not a pile of attempts.
        var user = Customer();
        user.AttachDocument(CustomerDocumentType.DrivingLicenceFront, "customers/abc/old.jpg", "image/jpeg", 10, Now);

        var superseded = user.AttachDocument(
            CustomerDocumentType.DrivingLicenceFront, "customers/abc/new.jpg", "image/jpeg", 20, Now);

        Assert.Equal("customers/abc/old.jpg", superseded);
        var document = Assert.Single(user.Documents);
        Assert.Equal("customers/abc/new.jpg", document.StorageKey);
    }

    [Fact]
    public void A_local_renter_is_complete_with_both_licence_sides_and_a_national_id()
    {
        var user = Customer();

        user.AttachDocument(CustomerDocumentType.DrivingLicenceFront, "customers/a/1.jpg", "image/jpeg", 10, Now);
        Assert.False(user.HasCompleteRenterDocuments);

        user.AttachDocument(CustomerDocumentType.DrivingLicenceBack, "customers/a/2.jpg", "image/jpeg", 10, Now);
        Assert.False(user.HasCompleteRenterDocuments);

        user.AttachDocument(CustomerDocumentType.NationalId, "customers/a/3.jpg", "image/jpeg", 10, Now);
        Assert.True(user.HasCompleteRenterDocuments);
    }

    [Fact]
    public void A_passport_satisfies_the_identity_requirement_for_a_foreign_renter()
    {
        var user = Customer();
        user.AttachDocument(CustomerDocumentType.DrivingLicenceFront, "customers/a/1.jpg", "image/jpeg", 10, Now);
        user.AttachDocument(CustomerDocumentType.DrivingLicenceBack, "customers/a/2.jpg", "image/jpeg", 10, Now);
        user.AttachDocument(CustomerDocumentType.Passport, "customers/a/3.jpg", "image/jpeg", 10, Now);

        Assert.True(user.HasCompleteRenterDocuments);
    }

    [Fact]
    public void A_document_without_content_is_a_programming_error_not_a_user_error()
    {
        var user = Customer();

        Assert.Throws<Khadra.Domain.Common.DomainException>(() =>
            user.AttachDocument(CustomerDocumentType.Passport, "customers/a/1.jpg", "image/jpeg", 0, Now));
        Assert.Throws<Khadra.Domain.Common.DomainException>(() =>
            user.AttachDocument(CustomerDocumentType.Passport, "  ", "image/jpeg", 10, Now));
    }

    [Fact]
    public void A_document_can_be_found_by_id_and_only_its_owners_documents_are_visible()
    {
        var user = Customer();
        user.AttachDocument(CustomerDocumentType.NationalId, "customers/a/1.jpg", "image/jpeg", 10, Now);
        var document = user.Documents.Single();

        Assert.NotNull(user.FindDocument(document.Id));
        Assert.Null(user.FindDocument(Khadra.Domain.Common.Id.New()));
    }
}
