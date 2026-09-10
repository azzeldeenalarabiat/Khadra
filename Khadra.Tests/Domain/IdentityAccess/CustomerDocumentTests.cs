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

    // ── What is still outstanding ───────────────────────────────────────────────────────────────
    //
    // The aggregate answers this, rather than each caller working it out, because TWO screens ask:
    // the customer's own checklist and the gallery's handover panel. Stated twice, the rule would
    // drift -- and the way it would drift is one of them being told the paperwork is complete.

    [Fact]
    public void A_renter_who_has_filed_nothing_is_missing_all_three()
    {
        var user = Customer();

        Assert.Equal(
            ["DrivingLicenceFront", "DrivingLicenceBack", "NationalId"],
            user.MissingRenterDocumentTypes().Select(type => type.Name));
    }

    [Fact]
    public void The_identity_slot_names_the_document_this_particular_renter_owes()
    {
        // Spec 5.1: one requirement with two possible answers -- a national ID for a local renter, a
        // passport for a foreign one. Naming both would tell a customer to file two documents when
        // either will do, and tell a gallery that something is absent when nothing is.
        var local = Customer();
        var foreigner = Foreigner();

        Assert.Contains(CustomerDocumentType.NationalId, local.MissingRenterDocumentTypes());
        Assert.DoesNotContain(CustomerDocumentType.Passport, local.MissingRenterDocumentTypes());

        Assert.Contains(CustomerDocumentType.Passport, foreigner.MissingRenterDocumentTypes());
        Assert.DoesNotContain(CustomerDocumentType.NationalId, foreigner.MissingRenterDocumentTypes());
    }

    [Fact]
    public void A_foreign_renters_passport_closes_the_identity_slot()
    {
        var user = Foreigner();
        user.AttachDocument(CustomerDocumentType.Passport, "customers/a/1.jpg", "image/jpeg", 10, Now);

        Assert.DoesNotContain(CustomerDocumentType.Passport, user.MissingRenterDocumentTypes());
        Assert.Equal(
            ["DrivingLicenceFront", "DrivingLicenceBack"],
            user.MissingRenterDocumentTypes().Select(type => type.Name));
    }

    [Fact]
    public void Nothing_is_missing_once_the_set_is_complete_and_the_two_answers_agree()
    {
        // The pair that must never disagree: an empty missing list and a complete record are the
        // same fact, and a screen reads one or the other depending on what it is saying.
        var user = Customer();
        user.AttachDocument(CustomerDocumentType.DrivingLicenceFront, "customers/a/1.jpg", "image/jpeg", 10, Now);
        user.AttachDocument(CustomerDocumentType.DrivingLicenceBack, "customers/a/2.jpg", "image/jpeg", 10, Now);
        user.AttachDocument(CustomerDocumentType.NationalId, "customers/a/3.jpg", "image/jpeg", 10, Now);

        Assert.Empty(user.MissingRenterDocumentTypes());
        Assert.True(user.HasCompleteRenterDocuments);
    }

    private static User Foreigner() =>
        User.RegisterCustomer(
            Khadra.Domain.IdentityAccess.EmailAddress.Create("visitor@example.com").Value,
            Khadra.Domain.IdentityAccess.PhoneNumber.Create("0791111111").Value,
            Khadra.Domain.IdentityAccess.PersonName.Create("Sara Haddad").Value,
            Khadra.Domain.IdentityAccess.PasswordHash.FromHash("hash"),
            Now,
            Users.AdultBirthDate,
            isForeignNational: true);
}
