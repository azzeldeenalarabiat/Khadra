using Khadra.Application.Bookings.ReadModels;
using Khadra.Application.Bookings.Reminders;
using Khadra.Application.Common.Ports;
using Khadra.Application.Notifications;
using Khadra.Domain.Bookings;
using Khadra.Domain.Bookings.Repositories;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess.Repositories;
using Khadra.Domain.Notifications;
using Khadra.Domain.Notifications.Repositories;
using Khadra.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Khadra.Tests.Application.Bookings;

/// <summary>Reminders: once per booking and moment, stating the booking's own time, never a false "pay".</summary>
public sealed class SendDueRemindersTests
{
    private static readonly DateTimeOffset Now = Build.Now;

    private readonly IReminderCandidateReader _candidates = Substitute.For<IReminderCandidateReader>();
    private readonly IBookingReminderRepository _reminders = Substitute.For<IBookingReminderRepository>();
    private readonly IBookingReader _bookings = Substitute.For<IBookingReader>();
    private readonly INotifier _notifier = Substitute.For<INotifier>();
    private readonly IReminderSettings _settings = Substitute.For<IReminderSettings>();
    private readonly IPaymentProvider _payments = Substitute.For<IPaymentProvider>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly List<Notification> _told = [];
    private readonly List<BookingReminder> _recorded = [];

    public SendDueRemindersTests()
    {
        _settings.PaymentLead.Returns(TimeSpan.FromMinutes(30));
        _settings.PickupLead.Returns(TimeSpan.FromHours(1));
        _settings.ReturnLead.Returns(TimeSpan.FromHours(1));
        _payments.Mode.Returns(PaymentMode.Sandbox);
        _bookings.ContextAsync(Arg.Any<Id>(), Arg.Any<CancellationToken>())
            .Returns(new BookingContext(null, "Petra Rentals", false, null, "Sami", false, null, null));
        _candidates.ListDueAsync(Arg.Any<ReminderKind>(), Arg.Any<DateTimeOffset>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns([]);
        _notifier.When(n => n.Raise(Arg.Any<Notification>())).Do(call => _told.Add(call.Arg<Notification>()));
        _reminders.When(r => r.AddAsync(Arg.Any<BookingReminder>(), Arg.Any<CancellationToken>()))
            .Do(call => _recorded.Add(call.Arg<BookingReminder>()));
    }

    private ReminderCandidate Candidate(ReminderKind kind, TimeSpan inFuture)
    {
        var candidate = new ReminderCandidate(Id.New(), Id.New(), "KH-24-0007", Now.Add(inFuture));
        _candidates.ListDueAsync(kind, Now, Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns([candidate]);
        return candidate;
    }

    private Task<CSharpFunctionalExtensions.Result<ReminderReport, Error>> RunAsync() =>
        new SendDueRemindersHandler(
                _candidates, _reminders, _bookings,
                new DealerTeamNotifier(_notifier, Substitute.For<IUserRepository>()),
                _settings, _payments, _unitOfWork, new TestClock(Now), NullLogger<SendDueRemindersHandler>.Instance)
            .Handle(new SendDueRemindersCommand(), CancellationToken.None);

    [Fact]
    public async Task A_pickup_reminder_records_the_moment_and_tells_the_customer_that_moment()
    {
        var candidate = Candidate(ReminderKind.Pickup, TimeSpan.FromMinutes(59));

        var report = (await RunAsync()).Value;

        Assert.Equal(1, report.Pickup);
        var reminder = Assert.Single(_recorded);
        Assert.Same(ReminderKind.Pickup, reminder.Kind);
        Assert.Equal(candidate.AnchorAt, reminder.AnchorAt);

        var told = Assert.Single(_told);
        Assert.Same(NotificationKind.YourPickupReminder, told.Kind);
        Assert.Equal(candidate.CustomerId, told.RecipientUserId);
        Assert.Equal(candidate.AnchorAt, told.DueAt);
        Assert.Equal("Petra Rentals", told.ActorName);
        Assert.Contains(NotificationChannel.Email, told.Kind.DeliveredOn());
        Assert.Contains(NotificationChannel.Push, told.Kind.DeliveredOn());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Return_and_payment_reminders_carry_their_own_kinds()
    {
        Candidate(ReminderKind.Return, TimeSpan.FromMinutes(40));
        Candidate(ReminderKind.Payment, TimeSpan.FromMinutes(20));

        var report = (await RunAsync()).Value;

        Assert.Equal((1, 0, 1), (report.Payment, report.Pickup, report.Return));
        Assert.Contains(_told, n => n.Kind == NotificationKind.YourReturnReminder);
        Assert.Contains(_told, n => n.Kind == NotificationKind.YourPaymentReminder);
    }

    [Fact]
    public async Task With_no_payment_provider_nobody_is_told_to_pay()
    {
        _payments.Mode.Returns(PaymentMode.None);
        Candidate(ReminderKind.Payment, TimeSpan.FromMinutes(20));

        var report = (await RunAsync()).Value;

        Assert.Equal(0, report.Payment);
        Assert.Empty(_told);
        await _candidates.DidNotReceive().ListDueAsync(ReminderKind.Payment, Arg.Any<DateTimeOffset>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Losing_the_race_to_another_process_stops_the_pass_without_a_second_send()
    {
        Candidate(ReminderKind.Pickup, TimeSpan.FromMinutes(30));
        Candidate(ReminderKind.Return, TimeSpan.FromMinutes(30));
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns<int>(_ => throw new UniqueConstraintConflictException("booking_reminders"));

        var report = (await RunAsync()).Value;

        Assert.Equal(0, report.Pickup);
        Assert.Equal(0, report.Return);
        // The return candidate is left for the next pass rather than saved alongside a rejected row.
        await _candidates.DidNotReceive().ListDueAsync(ReminderKind.Return, Arg.Any<DateTimeOffset>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void A_reminder_can_only_be_about_a_moment_still_ahead()
    {
        Assert.Throws<DomainException>(() => BookingReminder.Record(Id.New(), ReminderKind.Pickup, Now, Now));
        Assert.Throws<DomainException>(() => BookingReminder.Record(Id.Empty, ReminderKind.Pickup, Now.AddHours(1), Now));
    }
}
