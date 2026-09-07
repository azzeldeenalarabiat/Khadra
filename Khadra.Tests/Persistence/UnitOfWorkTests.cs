using Khadra.Application.Common;
using Khadra.Domain.Bookings;
using Khadra.Domain.Bookings.Events;
using Khadra.Domain.Common;
using Khadra.Infrastructure.Persistence;
using Khadra.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Khadra.Tests.Persistence;

/// <summary>
/// When domain events are allowed out.
/// </summary>
/// <remarks>
/// The unit of work promises that a side effect never runs for work that was rolled back. That is
/// easy while every use case is one save; it stops being automatic the moment a handler opens a
/// transaction and saves twice, because the first save is not durable until the commit.
///
/// The booking-creation handler is exactly that shape — expire stale holds, save, insert, save — and
/// its second save can be refused by the exclusion constraint. Dispatching as each save returned
/// would tell the world a booking had expired that the rollback then un-expired.
/// </remarks>
public sealed class UnitOfWorkTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<KhadraDbContext> _options;
    private readonly IDomainEventDispatcher _dispatcher = Substitute.For<IDomainEventDispatcher>();
    private readonly List<IDomainEvent> _dispatched = [];

    public UnitOfWorkTests()
    {
        _connection.Open();
        _options = new DbContextOptionsBuilder<KhadraDbContext>()
            .UseSqlite(_connection)
            .UseSnakeCaseNamingConvention()
            .Options;
        using var context = new KhadraDbContext(_options);
        context.Database.EnsureCreated();

        _dispatcher
            .When(dispatcher => dispatcher.DispatchAsync(
                Arg.Any<IReadOnlyCollection<IDomainEvent>>(), Arg.Any<CancellationToken>()))
            .Do(call => _dispatched.AddRange(call.Arg<IReadOnlyCollection<IDomainEvent>>()));
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task An_ordinary_save_dispatches_immediately()
    {
        await using var context = new KhadraDbContext(_options);
        var unitOfWork = new UnitOfWork(context, _dispatcher);
        context.Bookings.Add(Unsaved());

        await unitOfWork.SaveChangesAsync();

        Assert.Contains(_dispatched, domainEvent => domainEvent is BookingCreated);
    }

    [Fact]
    public async Task Events_from_a_save_inside_a_transaction_wait_for_the_commit()
    {
        await using var context = new KhadraDbContext(_options);
        var unitOfWork = new UnitOfWork(context, _dispatcher);
        var seenInside = 0;

        await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            context.Bookings.Add(Unsaved());
            await unitOfWork.SaveChangesAsync(token);
            // The row is written but not durable: the transaction can still roll back under it.
            seenInside = _dispatched.Count;
        });

        Assert.Equal(0, seenInside);
        Assert.Contains(_dispatched, domainEvent => domainEvent is BookingCreated);
    }

    /// <summary>
    /// The case this exists for: an early save succeeds, a later one fails, and the whole thing is
    /// undone. Nothing may have been told about the part that briefly existed.
    /// </summary>
    [Fact]
    public async Task A_rolled_back_transaction_dispatches_nothing_at_all()
    {
        await using var context = new KhadraDbContext(_options);
        var unitOfWork = new UnitOfWork(context, _dispatcher);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                context.Bookings.Add(Unsaved());
                await unitOfWork.SaveChangesAsync(token);
                throw new InvalidOperationException("the second write was refused");
            }));

        Assert.Empty(_dispatched);
        await using var reader = new KhadraDbContext(_options);
        Assert.Empty(await reader.Bookings.ToListAsync());
    }

    [Fact]
    public async Task Every_save_in_one_transaction_is_released_together_after_the_commit()
    {
        await using var context = new KhadraDbContext(_options);
        var unitOfWork = new UnitOfWork(context, _dispatcher);

        await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            context.Bookings.Add(Unsaved());
            await unitOfWork.SaveChangesAsync(token);
            context.Bookings.Add(Unsaved());
            await unitOfWork.SaveChangesAsync(token);
        });

        Assert.Equal(2, _dispatched.OfType<BookingCreated>().Count());
    }

    /// <summary>A second transaction must not inherit the first one's released events.</summary>
    [Fact]
    public async Task Released_events_are_not_dispatched_twice()
    {
        await using var context = new KhadraDbContext(_options);
        var unitOfWork = new UnitOfWork(context, _dispatcher);

        await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            context.Bookings.Add(Unsaved());
            await unitOfWork.SaveChangesAsync(token);
        });
        await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            context.Bookings.Add(Unsaved());
            await unitOfWork.SaveChangesAsync(token);
        });

        Assert.Equal(2, _dispatched.OfType<BookingCreated>().Count());
    }

    // A booking that still carries the events Create raised; the factory clears them by default.
    private static Booking Unsaved() =>
        Booking.Create(
            Id.New(), Id.New(), Id.New(),
            Build.Period(Build.Now.AddDays(7)),
            PickupMethod.SelfPickup,
            null,
            Build.Pricing(days: 3, pickupDate: Build.AmmanDate(Build.Now.AddDays(7))),
            Build.Terms(),
            PaymentOption.DepositOnly,
            Build.Now).Value;
}
