using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Infrastructure.Persistence;
using Khadra.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Tests.Persistence;

/// <summary>
/// The penalty's reason CODE, through the real EF model.
/// </summary>
/// <remarks>
/// <para>
/// It lives inside the `bookings.penalty` JSON document, which is exactly where properties have gone
/// missing before: EF includes a property of a `ToJson` value object only when it is mapped by hand,
/// and our value objects have no setters. A domain test cannot see that — it never opens a DbContext —
/// so the round trip is asserted here.
/// </para>
/// <para>
/// The second test is the whole reason the code is nullable: every booking assessed before codes
/// existed has a document without one, and it must load, keep its frozen sentence, and say plainly
/// that it has no code — never be guessed at from the text.
/// </para>
/// </remarks>
public sealed class PenaltyReasonPersistenceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<KhadraDbContext> _options;

    public PenaltyReasonPersistenceTests()
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

    private KhadraDbContext NewContext() => new(_options);

    /// <summary>A booking cancelled past its free window: a real assessment, with a real code.</summary>
    private async Task<Booking> SaveCancelledAsync()
    {
        var booking = Build.ConfirmedBooking();
        Assert.True(booking
            .Cancel(BookingParty.Customer, Id.New(), "Changed plans.", booking.FreeCancellationDeadline!.Value.AddMinutes(1))
            .IsSuccess);

        await using var write = NewContext();
        write.Bookings.Add(booking);
        await write.SaveChangesAsync();
        return booking;
    }

    [Fact]
    public async Task The_code_survives_the_round_trip_beside_the_sentence()
    {
        var booking = await SaveCancelledAsync();

        await using var read = NewContext();
        var stored = await read.Bookings.SingleAsync(candidate => candidate.Id == booking.Id);

        Assert.NotNull(stored.Penalty);
        Assert.Same(PenaltyReason.CustomerCancelledAfterFreeWindow, stored.Penalty!.ReasonCode);
        Assert.Equal(PenaltyReason.CustomerCancelledAfterFreeWindow.Sentence, stored.Penalty.Reason);
        Assert.Equal(booking.Penalty!.AssessedAt, stored.Penalty.AssessedAt);
    }

    [Fact]
    public async Task A_booking_assessed_before_codes_existed_still_loads_with_its_sentence()
    {
        var booking = await SaveCancelledAsync();

        // Exactly what an older row looks like: the document, minus the property that did not exist
        // when it was written. Nothing rewrites those rows, so this is the shape that must load.
        await using (var edit = NewContext())
        {
            var removed = await edit.Database.ExecuteSqlRawAsync(
                "UPDATE bookings SET penalty = json_remove(penalty, '$.ReasonCode') WHERE id = {0}",
                booking.Id.Value);
            Assert.Equal(1, removed);
        }

        await using var read = NewContext();
        var stored = await read.Bookings.SingleAsync(candidate => candidate.Id == booking.Id);

        Assert.NotNull(stored.Penalty);
        Assert.Null(stored.Penalty!.ReasonCode);
        // The sentence is untouched: it is what a client falls back to, and what the record has held
        // since the day it was written.
        Assert.Equal(PenaltyReason.CustomerCancelledAfterFreeWindow.Sentence, stored.Penalty.Reason);
        Assert.Same(BookingParty.Customer, stored.Penalty.AttributedTo);
    }

    /// <summary>
    /// A code written by a NEWER build, read by this one: the booking still loads.
    /// </summary>
    /// <remarks>
    /// The other direction of the same compatibility question. `FromName` throws on an unknown name,
    /// and a throw while materialising would take the whole aggregate with it — the booking screen,
    /// the cancellation preview, the dispute workspace — over a label. It reads as no code instead,
    /// and the frozen sentence is what a client shows, exactly as for a pre-code booking.
    /// </remarks>
    [Fact]
    public async Task A_reason_this_build_has_never_heard_of_does_not_make_the_booking_unloadable()
    {
        var booking = await SaveCancelledAsync();

        await using (var edit = NewContext())
        {
            var changed = await edit.Database.ExecuteSqlRawAsync(
                "UPDATE bookings SET penalty = json_set(penalty, '$.ReasonCode', 'SomethingAddedLater') WHERE id = {0}",
                booking.Id.Value);
            Assert.Equal(1, changed);
        }

        await using var read = NewContext();
        var stored = await read.Bookings.SingleAsync(candidate => candidate.Id == booking.Id);

        Assert.NotNull(stored.Penalty);
        Assert.Null(stored.Penalty!.ReasonCode);
        Assert.Equal(PenaltyReason.CustomerCancelledAfterFreeWindow.Sentence, stored.Penalty.Reason);
    }

    /// <summary>The code is stored by NAME: renaming a member would make these bookings unloadable.</summary>
    [Fact]
    public async Task The_document_holds_the_code_by_its_name()
    {
        var booking = await SaveCancelledAsync();

        await using var read = NewContext();
        var document = await read.Database
            .SqlQueryRaw<string>("SELECT penalty AS Value FROM bookings WHERE id = {0}", booking.Id.Value)
            .SingleAsync();

        // The literal, not nameof: a rename must fail here rather than follow the member silently.
        Assert.Contains("CustomerCancelledAfterFreeWindow", document, StringComparison.Ordinal);
    }
}
