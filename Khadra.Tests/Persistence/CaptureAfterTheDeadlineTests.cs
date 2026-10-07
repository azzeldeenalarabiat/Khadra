using CSharpFunctionalExtensions;
using Khadra.Application.Bookings;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.FinancialDocuments.Composition;
using Khadra.Application.Notifications;
using Khadra.Application.Payments.ReceiveProviderEvent;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Domain.Notifications;
using Khadra.Domain.Payments;
using Khadra.Infrastructure.Persistence;
using Khadra.Infrastructure.Persistence.Repositories;
using Khadra.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Khadra.Tests.Persistence;

/// <summary>
/// Checklist 234's plainest case, end to end on the real repositories (Wave 4): a capture landing seconds after the
/// payment deadline, on a booking the sweep has not reached.
/// </summary>
/// <remarks>
/// The webhook loads the booking through the settling seam, which expires it there and then; the capture cannot
/// confirm an expired booking, so it is orphaned and owed back; and one save commits the expiry, the orphan and the
/// receipt. Before Wave 4 that save told nobody the booking had expired — not the customer whose money is coming
/// back, not the office whose approval lapsed. Every handler test substitutes the booking repository, so only a run
/// over the real seam can show it.
/// </remarks>
public sealed class CaptureAfterTheDeadlineTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<KhadraDbContext> _options;

    public CaptureAfterTheDeadlineTests()
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

    [Fact]
    public async Task A_capture_just_after_the_deadline_expires_the_booking_and_announces_it_once()
    {
        var (booking, payment, office) = await CaptureJustAfterTheDeadlineAsync(deadline => new TestClock(deadline.AddSeconds(30)));

        await using var read = new KhadraDbContext(_options);
        var stored = await read.Bookings.AsNoTracking().SingleAsync(row => row.Id == booking.Id);
        Assert.Same(BookingStatus.Expired, stored.Status);
        var orphaned = await new PaymentRepository(read).GetByIdAsync(payment.Id);
        Assert.Same(PaymentStatus.Orphaned, orphaned!.Status);
        Assert.Equal("BookingExpired", orphaned.OrphanReason);
        Assert.Same(RefundReason.OrphanedCapture, Assert.Single(orphaned.Refunds).Reason);
        Assert.Same(ProviderEventOutcome.Orphaned, (await read.ProviderEventReceipts.AsNoTracking().SingleAsync()).Outcome);

        // Told once each, in the save that recorded the expiry.
        var told = await read.Notifications.AsNoTracking().Where(row => row.SubjectId == booking.Id).ToListAsync();
        var customer = Assert.Single(told, row => row.Kind == NotificationKind.YourBookingExpired);
        Assert.Equal(booking.CustomerId, customer.RecipientUserId);
        var owner = Assert.Single(told, row => row.Kind == NotificationKind.BookingExpiredUnpaid);
        Assert.Equal(office.OwnerUserId, owner.RecipientUserId);
        Assert.True(owner.IsFromPlatform);
    }

    /// <summary>
    /// The receipt for that orphaned capture states the booking as it stood after the payment — Expired — with a clock
    /// that moves between readings, as the real one does (the advisor's review of Wave 4's payments half).
    /// </summary>
    /// <remarks>
    /// A frozen clock hid it. The handler read the instant before loading the booking, and the load expired the booking
    /// with its own, later reading; the orphan, stamped with the earlier instant, preceded the expiry that caused it,
    /// and the receipt said Approved. Every reading here is a millisecond after the last.
    /// </remarks>
    [Fact]
    public async Task With_a_clock_that_moves_the_orphaned_captures_receipt_states_the_booking_expired()
    {
        var (booking, payment, _) = await CaptureJustAfterTheDeadlineAsync(deadline => new TickingClock(deadline.AddSeconds(30)));

        await using var read = new KhadraDbContext(_options);
        var stored = (await new BookingRepository(read).GetByIdAsync(booking.Id))!;
        var orphaned = (await new PaymentRepository(read).GetByIdAsync(payment.Id))!;
        var expiry = Assert.Single(stored.StatusHistory, change => change.To == BookingStatus.Expired);

        Assert.True(orphaned.OrphanedAt >= expiry.OccurredAt, "The orphan must not precede the expiry it was orphaned by.");
        Assert.Same(BookingStatus.Expired, FinancialDocumentComposer.StatusAfterPayment(stored, orphaned));
    }

    /// <summary>
    /// An approved, unpaid booking whose deadline has just passed, unreached by the sweep, and a capture notice for its
    /// checkout handled through the real repositories and the real settling seam.
    /// </summary>
    private async Task<(Booking Booking, Payment Payment, Dealer Office)> CaptureJustAfterTheDeadlineAsync(
        Func<DateTimeOffset, IClock> clockAt)
    {
        var office = Build.ApprovedDealer();
        var booking = Build.ApprovedBooking(Build.Now, dealerId: office.Id);
        booking.ClearDomainEvents();
        var deposit = Money.Create(booking.Pricing.DepositAmount.Amount, booking.Pricing.DepositAmount.CurrencyCode);
        var payment = Payment.Open(booking.Id, booking.CustomerId, deposit, TestPayments.TestProviderName, Build.Now.AddMinutes(30), Build.Now);
        Assert.True(payment.AttachProviderSession("sess_late", "https://provider.test/sess_late").IsSuccess);
        await using (var seed = new KhadraDbContext(_options))
        {
            seed.Dealers.Add(office);
            seed.Bookings.Add(booking);
            seed.Payments.Add(payment);
            await seed.SaveChangesAsync();
        }

        var deadline = booking.PaymentDeadline!.Value;
        var clock = clockAt(deadline);
        var provider = Substitute.For<IPaymentProvider>();
        provider.Name.Returns(TestPayments.TestProviderName);
        provider.ParseEvent(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>())
            .Returns(Result.Success<ProviderEvent, Error>(TestPayments.Captured(
                "sess_late", Money.Create(deposit.Amount, deposit.CurrencyCode), deadline.AddSeconds(20), "evt_late", "cap_late")));

        await using (var context = new KhadraDbContext(_options))
        {
            var team = new DealerTeamNotifier(new Notifier(context), new UserRepository(context));
            var handler = new ReceiveProviderEventHandler(
                provider,
                new PaymentRepository(context),
                new ProviderEventReceiptRepository(context),
                new PaymentIncidentRepository(context),
                new SettlingBookingRepository(new BookingRepository(context), clock, new BookingExpiryAnnouncer(team, new DealerRepository(context))),
                new DealerRepository(context),
                team,
                clock,
                TestPayments.Settings(),
                new UnitOfWork(context, Substitute.For<IDomainEventDispatcher>()),
                NullLogger<ReceiveProviderEventHandler>.Instance);

            Assert.True((await handler.Handle(new ReceiveProviderEventCommand("{}", new Dictionary<string, string>()), CancellationToken.None)).IsSuccess);
        }

        return (booking, payment, office);
    }

    /// <summary>A clock that moves on every reading, as the system clock does between two database round trips.</summary>
    private sealed class TickingClock(DateTimeOffset start) : IClock
    {
        private DateTimeOffset _next = start;

        public DateTimeOffset UtcNow
        {
            get
            {
                var now = _next;
                _next = _next.AddMilliseconds(1);
                return now;
            }
        }
    }
}
