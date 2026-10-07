using Khadra.Application.Auditing;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.IdentityAccess.AdminCustomers;
using Khadra.Application.IdentityAccess.ReadModels;
using Khadra.Application.Notifications;
using Khadra.Domain.Auditing;
using Khadra.Domain.Auditing.Repositories;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.IdentityAccess.Repositories;
using Khadra.Domain.Notifications;
using Khadra.Domain.Notifications.Repositories;
using Khadra.Tests.Support;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Khadra.Tests.Application.IdentityAccess;

/// <summary>
/// An administrator opens and rejects a renter's document (Wave 4, W4-9; checklist 27, owner D3).
/// </summary>
public sealed class AdminCustomerDocumentTests
{
    private static readonly DateTimeOffset Now = Users.Now.AddDays(2);

    private readonly User _customer = Users.Customer();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IDocumentStorage _storage = Substitute.For<IDocumentStorage>();
    private readonly IDocumentAccessLog _accessLog = Substitute.For<IDocumentAccessLog>();
    private readonly IAuditTrail _auditTrail = Substitute.For<IAuditTrail>();
    private readonly INotifier _notifier = Substitute.For<INotifier>();
    private readonly ICustomerAdminReader _reader = Substitute.For<ICustomerAdminReader>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentActor _admin = Substitute.For<ICurrentActor>();
    private readonly List<DocumentAccessEntry> _views = [];
    private readonly List<AuditEntry> _audited = [];
    private readonly List<Notification> _told = [];

    public AdminCustomerDocumentTests()
    {
        _customer.AttachDocument(CustomerDocumentType.DrivingLicenceFront, "customers/a/1.jpg", "image/jpeg", 10, Users.Now);
        _customer.AttachDocument(CustomerDocumentType.DrivingLicenceBack, "customers/a/2.pdf", "application/pdf", 10, Users.Now);
        _customer.AttachDocument(CustomerDocumentType.NationalId, "customers/a/3.png", "image/png", 10, Users.Now);
        _users.GetByIdAsync(_customer.Id, Arg.Any<CancellationToken>()).Returns(_customer);
        _storage.OpenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ => new MemoryStream([1, 2, 3]));
        _accessLog.When(log => log.Record(Arg.Any<DocumentAccessEntry>())).Do(call => _views.Add(call.Arg<DocumentAccessEntry>()));
        // This administrator has opened the file being judged, unless a test says otherwise.
        _accessLog.AdministratorHasViewedAsync(
                Arg.Any<Id>(), Arg.Any<Id>(), Arg.Any<Id>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _auditTrail.When(trail => trail.Record(Arg.Any<AuditEntry>())).Do(call => _audited.Add(call.Arg<AuditEntry>()));
        _notifier.When(notifier => notifier.Raise(Arg.Any<Notification>())).Do(call => _told.Add(call.Arg<Notification>()));
        _admin.UserId.Returns(Id.New());
        _admin.Role.Returns(UserRole.Admin);
        _admin.Name.Returns("Dana Saleh");
        _admin.IsAuthenticated.Returns(true);
        _reader.GetAsync(_customer.Id, Arg.Any<CancellationToken>()).Returns(new CustomerProfile(
            _customer.Id.Value, "Ali Ahmad", "ali@example.com", "+962791234567", "Active", null, true, Users.Now, null,
            false, Users.Now, null, null, false, [], new CustomerBookingTotals(0, 0, 0, 0, 0)));
    }

    private AdminCustomerDocumentHandlers Handlers()
    {
        var clock = new TestClock(Now);
        return new AdminCustomerDocumentHandlers(
            _users,
            _storage,
            new DocumentAccessRecorder(_accessLog, _admin, clock),
            new AdminActionRecorder(_auditTrail, _admin, clock),
            new DealerTeamNotifier(_notifier, _users),
            _reader,
            clock,
            _unitOfWork);
    }

    private CustomerDocument Front => _customer.Documents.Single(document => document.Type == CustomerDocumentType.DrivingLicenceFront);

    // ── Opening ──────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Opening_a_document_records_the_view_before_the_file_is_served()
    {
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            // The view is staged by the time the save runs: it commits before a byte goes out.
            Assert.Single(_views);
            return Task.FromResult(1);
        });

        var opened = await Handlers().Handle(new OpenCustomerDocumentCommand(_customer.Id, Front.Id), CancellationToken.None);

        Assert.True(opened.IsSuccess);
        Assert.Equal("image/jpeg", opened.Value.ContentType);
        var view = Assert.Single(_views);
        Assert.Null(view.DealerId);
        Assert.Null(view.BookingId);
        Assert.Same(UserRole.Admin, view.ActorRole);
        Assert.Equal(_customer.Id, view.SubjectUserId);
        Assert.Equal(Front.Id, view.DocumentId);
        Assert.Equal(Front.UploadedAt, view.DocumentUploadedAt);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_view_that_cannot_be_recorded_is_not_served_and_the_file_is_closed()
    {
        var stream = new MemoryStream([1, 2, 3]);
        _storage.OpenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(stream);
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("The database is unreachable."));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Handlers().Handle(new OpenCustomerDocumentCommand(_customer.Id, Front.Id), CancellationToken.None));

        Assert.False(stream.CanRead);
    }

    [Fact]
    public async Task Another_customers_document_a_missing_file_and_a_non_customer_are_all_not_found_and_nothing_is_recorded()
    {
        var owner = User.RegisterDealerOwner(
            EmailAddress.Create("owner@gallery.jo").Value, PhoneNumber.Create("0795554444").Value,
            PersonName.Create("Rami Odeh").Value, PasswordHash.FromHash("hashed:x"), Users.Now);
        _users.GetByIdAsync(owner.Id, Arg.Any<CancellationToken>()).Returns(owner);
        _storage.OpenAsync("customers/a/2.pdf", Arg.Any<CancellationToken>()).Returns((Stream?)null);

        var notTheirs = await Handlers().Handle(new OpenCustomerDocumentCommand(_customer.Id, Id.New()), CancellationToken.None);
        var noFile = await Handlers().Handle(
            new OpenCustomerDocumentCommand(_customer.Id, _customer.Documents.Single(d => d.Type == CustomerDocumentType.DrivingLicenceBack).Id),
            CancellationToken.None);
        var notACustomer = await Handlers().Handle(new OpenCustomerDocumentCommand(owner.Id, Id.New()), CancellationToken.None);

        Assert.Equal(IdentityErrors.DocumentNotFound, notTheirs.Error);
        Assert.Equal(IdentityErrors.DocumentNotFound, noFile.Error);
        Assert.Equal(IdentityErrors.UserNotFound, notACustomer.Error);
        Assert.Empty(_views);
    }

    // ── Rejecting ────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Rejecting_records_the_reason_audits_it_and_tells_the_customer_in_one_save()
    {
        var rejected = await Handlers().Handle(
            new RejectCustomerDocumentCommand(_customer.Id, Front.Id, "The photo is too blurred to read.", Front.UploadedAt),
            CancellationToken.None);

        Assert.True(rejected.IsSuccess);
        Assert.Same(CustomerDocumentStatus.Rejected, Front.Status);
        Assert.Equal("The photo is too blurred to read.", Front.ReviewNote);

        // Labelled by reference, never a name; the values carry the slot and its status before and after.
        var entry = Assert.Single(_audited);
        Assert.Same(AuditAction.CustomerDocumentRejected, entry.Action);
        Assert.Same(AuditEntityType.Customer, entry.EntityType);
        Assert.Equal(_customer.Id, entry.EntityId);
        Assert.Equal($"Customer {_customer.Id.Value:N}"[..17], entry.SubjectLabel);
        Assert.Equal("DrivingLicenceFront:PendingReview", entry.PreviousValue);
        Assert.Equal("DrivingLicenceFront:Rejected", entry.NewValue);
        Assert.Equal("The photo is too blurred to read.", entry.Reason);
        Assert.DoesNotContain("Ali", entry.SubjectLabel, StringComparison.Ordinal);

        // No subject, no reference, and from Khadra: installed apps print a reference raw, and route a "null" subject.
        var notice = Assert.Single(_told);
        Assert.Same(NotificationKind.YourDocumentRejected, notice.Kind);
        Assert.Equal(_customer.Id, notice.RecipientUserId);
        Assert.Null(notice.SubjectId);
        Assert.Null(notice.SubjectReference);
        Assert.True(notice.IsFromPlatform);

        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_file_replaced_since_it_was_opened_is_refused_and_nothing_is_written()
    {
        var viewed = Front.UploadedAt;
        _customer.AttachDocument(CustomerDocumentType.DrivingLicenceFront, "customers/a/9.jpg", "image/jpeg", 10, Users.Now.AddHours(1));

        var rejected = await Handlers().Handle(
            new RejectCustomerDocumentCommand(_customer.Id, Front.Id, "Blurred.", viewed), CancellationToken.None);

        Assert.Equal(IdentityErrors.DocumentChangedSinceViewed, rejected.Error);
        Assert.Empty(_audited);
        Assert.Empty(_told);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A rejection names a file its author looked at (the advisor's review): the profile showing it is not enough, and the
    /// log is asked about THIS administrator and THIS upload.
    /// </summary>
    [Fact]
    public async Task A_file_this_administrator_has_not_opened_is_not_rejected_and_nothing_is_written()
    {
        _accessLog.AdministratorHasViewedAsync(
                Arg.Any<Id>(), Arg.Any<Id>(), Arg.Any<Id>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var rejected = await Handlers().Handle(
            new RejectCustomerDocumentCommand(_customer.Id, Front.Id, "Blurred.", Front.UploadedAt), CancellationToken.None);

        Assert.Equal(IdentityErrors.DocumentNotViewed, rejected.Error);
        Assert.Same(CustomerDocumentStatus.PendingReview, Front.Status);
        Assert.Empty(_audited);
        Assert.Empty(_told);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _accessLog.Received(1).AdministratorHasViewedAsync(
            _admin.UserId!.Value, _customer.Id, Front.Id, Front.UploadedAt, Arg.Any<CancellationToken>());
    }

    /// <summary>The race the document's own token catches: the customer's upload committed between this load and this save.</summary>
    [Fact]
    public async Task A_rejection_that_loses_the_race_to_a_new_upload_says_the_file_changed()
    {
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new ConcurrencyConflictException());

        var rejected = await Handlers().Handle(
            new RejectCustomerDocumentCommand(_customer.Id, Front.Id, "Blurred.", Front.UploadedAt), CancellationToken.None);

        Assert.Equal(IdentityErrors.DocumentChangedSinceViewed, rejected.Error);
    }

    [Fact]
    public void A_rejection_needs_a_reason_within_its_column_and_the_upload_it_judged()
    {
        var validator = new RejectCustomerDocumentCommandValidator();
        var document = Id.New();

        Assert.False(validator.Validate(new RejectCustomerDocumentCommand(_customer.Id, document, "  ", Now)).IsValid);
        Assert.False(validator.Validate(new RejectCustomerDocumentCommand(_customer.Id, document, new string('x', 501), Now)).IsValid);
        Assert.False(validator.Validate(new RejectCustomerDocumentCommand(_customer.Id, document, "Blurred.", default)).IsValid);
        Assert.True(validator.Validate(new RejectCustomerDocumentCommand(_customer.Id, document, $"  {new string('x', 500)}  ", Now)).IsValid);
    }
}
