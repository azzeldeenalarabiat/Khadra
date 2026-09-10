using Khadra.Domain.Auditing;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Infrastructure.Persistence;
using Khadra.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Tests.Persistence;

/// <summary>
/// The two records this feature writes, through a real EF model.
/// </summary>
/// <remarks>
/// SQLite in memory, like the rest of the persistence suite. The database TRIGGER that refuses an
/// update, a delete or a truncate exists only on Postgres and is exercised against the real database
/// during the walkthrough; what these pin is the half that has to hold everywhere — the model maps
/// every field, and <c>KhadraDbContext</c> itself refuses to rewrite an append-only record.
/// </remarks>
public sealed class DocumentAccessPersistenceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<KhadraDbContext> _options;

    private static readonly DateTimeOffset Now = Build.Now;

    public DocumentAccessPersistenceTests()
    {
        _connection.Open();
        _options = new DbContextOptionsBuilder<KhadraDbContext>()
            .UseSqlite(_connection)
            .UseSnakeCaseNamingConvention()
            .Options;
        using var context = new KhadraDbContext(_options);
        context.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private static DocumentAccessEntry Entry(
        DocumentAccessAction? action = null,
        Id? subjectUserId = null) =>
        DocumentAccessEntry.Record(
            action ?? DocumentAccessAction.Viewed,
            Id.New(),
            "Rami Haddad",
            UserRole.DealerOwner,
            Id.New(),
            Id.New(),
            subjectUserId ?? Id.New(),
            Id.New(),
            CustomerDocumentType.Passport,
            Now.AddDays(-2),
            Now,
            "correlation-1");

    [Fact]
    public async Task Every_field_of_a_disclosure_record_survives_a_round_trip()
    {
        // Every one, deliberately. A field silently dropped by the model would be discovered by a
        // customer asking a question this table could no longer answer.
        var subject = Id.New();
        var entry = Entry(DocumentAccessAction.Reviewed, subject);
        await using (var write = new KhadraDbContext(_options))
        {
            write.DocumentAccessEntries.Add(entry);
            await write.SaveChangesAsync();
        }

        await using var read = new KhadraDbContext(_options);
        var stored = await read.DocumentAccessEntries.SingleAsync();

        Assert.Equal(entry.Id, stored.Id);
        Assert.Equal(entry.OccurredAt, stored.OccurredAt);
        Assert.Equal(entry.ActorUserId, stored.ActorUserId);
        Assert.Equal("Rami Haddad", stored.ActorName);
        Assert.Equal(UserRole.DealerOwner, stored.ActorRole);
        Assert.Equal(entry.DealerId, stored.DealerId);
        Assert.Equal(entry.BookingId, stored.BookingId);
        Assert.Equal(subject, stored.SubjectUserId);
        Assert.Equal(entry.DocumentId, stored.DocumentId);
        Assert.Equal(CustomerDocumentType.Passport, stored.DocumentType);
        Assert.Equal(entry.DocumentUploadedAt, stored.DocumentUploadedAt);
        Assert.Equal(DocumentAccessAction.Reviewed, stored.Action);
        Assert.Equal("correlation-1", stored.CorrelationId);
    }

    [Fact]
    public async Task A_stored_disclosure_record_cannot_be_edited()
    {
        await using (var write = new KhadraDbContext(_options))
        {
            write.DocumentAccessEntries.Add(Entry());
            await write.SaveChangesAsync();
        }

        await using var edit = new KhadraDbContext(_options);
        var stored = await edit.DocumentAccessEntries.SingleAsync();
        edit.Entry(stored).Property(nameof(DocumentAccessEntry.ActorName)).CurrentValue = "somebody else";

        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => edit.SaveChangesAsync());
        Assert.Contains("append-only", refusal.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_stored_disclosure_record_cannot_be_deleted()
    {
        await using (var write = new KhadraDbContext(_options))
        {
            write.DocumentAccessEntries.Add(Entry());
            await write.SaveChangesAsync();
        }

        await using var delete = new KhadraDbContext(_options);
        delete.DocumentAccessEntries.Remove(await delete.DocumentAccessEntries.SingleAsync());

        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => delete.SaveChangesAsync());
        Assert.Contains("append-only", refusal.Message, StringComparison.OrdinalIgnoreCase);

        // And it is still there afterwards.
        await using var read = new KhadraDbContext(_options);
        Assert.Equal(1, await read.DocumentAccessEntries.CountAsync());
    }

    [Fact]
    public async Task The_guard_still_covers_the_admin_audit_trail_it_was_written_for()
    {
        // The guard used to name AuditEntry in its own type argument. Generalising it to IAppendOnly
        // must not have quietly stopped covering the table it started with.
        await using (var write = new KhadraDbContext(_options))
        {
            write.AuditEntries.Add(AuditEntry.BySystem(
                AuditAction.BookingExpired, AuditEntityType.Booking, Id.New(), "KH-TEST", Now));
            await write.SaveChangesAsync();
        }

        await using var delete = new KhadraDbContext(_options);
        delete.AuditEntries.Remove(await delete.AuditEntries.SingleAsync());

        await Assert.ThrowsAsync<InvalidOperationException>(() => delete.SaveChangesAsync());
    }

    [Fact]
    public async Task A_dealer_review_round_trips_on_its_booking()
    {
        var booking = Build.ConfirmedBooking(Now);
        var documentId = Id.New();
        var uploadedAt = Now.AddDays(-1);
        var recorded = booking.RecordRenterDocumentReview(
            documentId,
            CustomerDocumentType.DrivingLicenceBack,
            uploadedAt,
            Id.New(),
            "Layla Haddad",
            Now);
        Assert.True(recorded.IsSuccess);

        await using (var write = new KhadraDbContext(_options))
        {
            write.Bookings.Add(booking);
            await write.SaveChangesAsync();
        }

        await using var read = new KhadraDbContext(_options);
        var stored = await read.Bookings
            .Include(candidate => candidate.RenterDocumentReviews)
            .SingleAsync(candidate => candidate.Id == booking.Id);

        var review = Assert.Single(stored.RenterDocumentReviews);
        Assert.Equal(booking.Id, review.BookingId);
        Assert.Equal(documentId, review.DocumentId);
        Assert.Equal(CustomerDocumentType.DrivingLicenceBack, review.DocumentType);
        Assert.Equal(uploadedAt, review.DocumentUploadedAt);
        Assert.Equal(recorded.Value.ReviewedByUserId, review.ReviewedByUserId);
        Assert.Equal("Layla Haddad", review.ReviewedByName);
        Assert.Equal(Now, review.ReviewedAt);
    }

    [Fact]
    public async Task Two_reviews_of_the_same_upload_cannot_both_be_stored()
    {
        // The floor under the handler's own check. Two clicks a millisecond apart both pass an
        // in-memory "already reviewed?" test; the unique index is what actually stops the second row.
        var booking = Build.ConfirmedBooking(Now);
        var documentId = Id.New();
        var uploadedAt = Now.AddDays(-1);
        booking.RecordRenterDocumentReview(
            documentId, CustomerDocumentType.NationalId, uploadedAt, Id.New(), "First", Now);

        await using (var write = new KhadraDbContext(_options))
        {
            write.Bookings.Add(booking);
            await write.SaveChangesAsync();
        }

        await using (var second = new KhadraDbContext(_options))
        {
            // Reaching past the aggregate on purpose: this is the race the aggregate cannot see.
            second.Set<RenterDocumentReview>().Add(Duplicate(booking.Id, documentId, uploadedAt));
            await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
        }

        // Read fresh: the refused insert left one row, not two.
        await using var read = new KhadraDbContext(_options);
        Assert.Equal(1, await read.Set<RenterDocumentReview>().CountAsync());
    }

    [Fact]
    public async Task A_review_of_a_different_upload_of_the_same_document_is_allowed()
    {
        // The other half of the key. A re-photographed licence needs its own look, and the record of
        // the old one stays on the books for the file it was actually about.
        var booking = Build.ConfirmedBooking(Now);
        var documentId = Id.New();
        booking.RecordRenterDocumentReview(
            documentId, CustomerDocumentType.NationalId, Now.AddDays(-1), Id.New(), "First", Now);
        booking.RecordRenterDocumentReview(
            documentId, CustomerDocumentType.NationalId, Now.AddHours(-1), Id.New(), "Second", Now);

        await using (var write = new KhadraDbContext(_options))
        {
            write.Bookings.Add(booking);
            await write.SaveChangesAsync();
        }

        await using var read = new KhadraDbContext(_options);
        var stored = await read.Bookings
            .Include(candidate => candidate.RenterDocumentReviews)
            .SingleAsync(candidate => candidate.Id == booking.Id);

        Assert.Equal(2, stored.RenterDocumentReviews.Count);
    }

    /// <summary>A second review row for the same upload, built the way a racing request would.</summary>
    private static RenterDocumentReview Duplicate(Id bookingId, Id documentId, DateTimeOffset uploadedAt)
    {
        var other = Build.ConfirmedBooking(Now);
        // The aggregate refuses a duplicate, so this borrows a row from a different booking and
        // rewrites its BookingId through EF -- exactly what two concurrent inserts would produce.
        other.RecordRenterDocumentReview(
            documentId, CustomerDocumentType.NationalId, uploadedAt, Id.New(), "Racer", Now);
        var review = other.RenterDocumentReviews.Single();
        typeof(RenterDocumentReview)
            .GetProperty(nameof(RenterDocumentReview.BookingId))!
            .SetValue(review, bookingId);
        return review;
    }
}
