using CSharpFunctionalExtensions;
using Khadra.Application.IdentityAccess.Documents;
using Khadra.Application.IdentityAccess.Dtos;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.IdentityAccess.Repositories;
using Khadra.Tests.Support;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Khadra.Tests.Application.IdentityAccess;

/// <summary>
/// A customer replaces a file while an administrator rejects the one it replaces (Wave 4, W4-9; the advisor's review).
/// The document row carries its own concurrency token, so one of the two saves is refused; the upload is the one that
/// tries again, because a new file starts the review over and the customer must never be told their upload conflicted.
/// </summary>
public sealed class DocumentUploadRaceTests
{
    private const string OldFile = "customers/a/old.jpg";

    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly FakeDocumentStorage _storage = new();

    // Two loads of one account: once the tracker is cleared, EF hands back a new instance read from the database.
    private readonly User _loaded = WithFront();
    private readonly User _reloaded = WithFront();

    public DocumentUploadRaceTests()
    {
        var front = Front(_reloaded);
        _reloaded.RejectDocument(front.Id, "The photo is too blurred to read.", front.UploadedAt);
        _users.GetByIdAsync(_loaded.Id, Arg.Any<CancellationToken>()).Returns(_loaded, _reloaded);
    }

    private static User WithFront()
    {
        var user = Users.Customer();
        user.AttachDocument(CustomerDocumentType.DrivingLicenceFront, OldFile, "image/jpeg", 10, Users.Now);
        return user;
    }

    private static CustomerDocument Front(User user) =>
        user.Documents.Single(document => document.Type == CustomerDocumentType.DrivingLicenceFront);

    private Task<Result<CustomerDocumentDto, Error>> Upload() =>
        new UploadCustomerDocumentHandler(_users, _storage, FakeDocumentPolicy.Default, new TestClock(Users.Now.AddDays(1)), _unitOfWork)
            .Handle(
                new UploadCustomerDocumentCommand(_loaded.Id, "DrivingLicenceFront", "front.jpg", "image/jpeg", 3, new MemoryStream([1, 2, 3])),
                CancellationToken.None);

    [Fact]
    public async Task An_upload_that_crosses_a_rejection_is_applied_again_to_the_account_read_afresh()
    {
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(
            _ => throw new ConcurrencyConflictException(),
            _ => Task.FromResult(1));

        var uploaded = await Upload();

        Assert.True(uploaded.IsSuccess);
        var newFile = Assert.Single(_storage.Saved).Key;
        var front = Front(_reloaded);
        Assert.Equal(newFile, front.StorageKey);
        Assert.Same(CustomerDocumentStatus.PendingReview, front.Status);
        Assert.Null(front.ReviewNote);
        Assert.Equal("PendingReview", uploaded.Value.Status);
        _unitOfWork.Received(1).DiscardChanges();
        // The rejected file is what the new one replaced, and it goes only once the new row is committed.
        Assert.Equal([OldFile], _storage.Deleted);
    }

    [Fact]
    public async Task An_upload_refused_twice_leaves_no_file_of_its_own_behind()
    {
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new ConcurrencyConflictException());

        await Assert.ThrowsAsync<ConcurrencyConflictException>(Upload);

        var newFile = Assert.Single(_storage.Saved).Key;
        Assert.Equal([newFile], _storage.Deleted);
    }

    [Fact]
    public async Task An_account_gone_by_the_retry_takes_its_new_file_with_it()
    {
        _users.GetByIdAsync(_loaded.Id, Arg.Any<CancellationToken>()).Returns(_loaded, (User?)null);
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new ConcurrencyConflictException());

        var uploaded = await Upload();

        Assert.Equal(IdentityErrors.UserNotFound, uploaded.Error);
        var newFile = Assert.Single(_storage.Saved).Key;
        Assert.Equal([newFile], _storage.Deleted);
    }
}
