using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Infrastructure.Persistence;
using Khadra.Infrastructure.Persistence.Repositories;
using Khadra.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Tests.Persistence;

/// <summary>Handover codes and the verification a handover record carries, through the real model.</summary>
public sealed class HandoverPersistenceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<KhadraDbContext> _options;

    public HandoverPersistenceTests()
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

    [Fact]
    public async Task The_current_code_is_the_newest_one_not_superseded()
    {
        var booking = Build.ConfirmedBooking();
        var older = HandoverCode.Issue(booking.Id, HandoverType.Pickup, new string('a', 64), Build.Now, TimeSpan.FromMinutes(15));
        var newer = HandoverCode.Issue(booking.Id, HandoverType.Pickup, new string('b', 64), Build.Now.AddMinutes(1), TimeSpan.FromMinutes(15));
        older.Supersede(Build.Now.AddMinutes(1));
        var returnCode = HandoverCode.Issue(booking.Id, HandoverType.Return, new string('c', 64), Build.Now.AddMinutes(2), TimeSpan.FromMinutes(15));
        await using (var context = NewContext())
        {
            context.Bookings.Add(booking);
            context.HandoverCodes.AddRange(older, newer, returnCode);
            await context.SaveChangesAsync();
        }

        await using var reader = NewContext();
        var repository = new HandoverCodeRepository(reader);
        var current = await repository.GetCurrentAsync(booking.Id, HandoverType.Pickup);
        Assert.Equal(newer.Id, current!.Id);
        Assert.Equal(new string('b', 64), current.CodeHash);
        Assert.Equal([newer.Id], (await repository.ListCurrentAsync(booking.Id, HandoverType.Pickup)).Select(c => c.Id));
        Assert.Null(await repository.GetCurrentAsync(Id.New(), HandoverType.Pickup));
    }

    [Fact]
    public async Task A_failed_attempt_survives_the_round_trip()
    {
        var booking = Build.ConfirmedBooking();
        var code = HandoverCode.Issue(booking.Id, HandoverType.Pickup, new string('a', 64), Build.Now, TimeSpan.FromMinutes(15));
        await using (var context = NewContext())
        {
            context.Bookings.Add(booking);
            context.HandoverCodes.Add(code);
            await context.SaveChangesAsync();
        }

        await using (var context = NewContext())
        {
            var loaded = await new HandoverCodeRepository(context).GetCurrentAsync(booking.Id, HandoverType.Pickup);
            loaded!.Verify(false, 5, Id.New(), Build.Now.AddMinutes(1));
            await context.SaveChangesAsync();
        }

        await using var reader = NewContext();
        Assert.Equal(1, (await reader.HandoverCodes.SingleAsync()).FailedAttempts);
    }

    [Fact]
    public async Task A_handover_keeps_how_it_was_verified_and_an_old_one_reads_as_none()
    {
        var verified = Build.ConfirmedBooking();
        var codeId = Id.New();
        verified.RecordPickup(BookingParty.Dealer, Id.New(), verified.Period.Start, proof: HandoverProof.ByCode(codeId));
        var unverified = Build.ConfirmedBooking();
        unverified.RecordPickup(BookingParty.Dealer, Id.New(), unverified.Period.Start,
            proof: HandoverProof.Unverified("Phone was dead; licence checked at counter.").Value);
        await using (var context = NewContext())
        {
            context.Bookings.AddRange(verified, unverified);
            await context.SaveChangesAsync();
        }

        await using var reader = NewContext();
        var first = (await reader.Bookings.Include(b => b.Handovers).SingleAsync(b => b.Id == verified.Id)).Handovers.Single();
        Assert.Same(HandoverVerification.Code, first.Verification);
        Assert.Equal(codeId, first.HandoverCodeId);
        Assert.Null(first.UnverifiedReason);

        var second = (await reader.Bookings.Include(b => b.Handovers).SingleAsync(b => b.Id == unverified.Id)).Handovers.Single();
        Assert.True(second.IsUnverified);
        Assert.Equal("Phone was dead; licence checked at counter.", second.UnverifiedReason);
    }
}
