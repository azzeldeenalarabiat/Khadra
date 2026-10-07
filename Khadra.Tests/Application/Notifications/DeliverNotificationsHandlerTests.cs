using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.Notifications.Delivery;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.IdentityAccess.Repositories;
using Khadra.Domain.Notifications;
using Khadra.Domain.Notifications.Repositories;
using Khadra.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Khadra.Tests.Application.Notifications;

/// <summary>
/// Working the outbox: every outcome a push or an email can have, and what the row says afterwards.
/// </summary>
public sealed class DeliverNotificationsHandlerTests
{
    private static readonly DateTimeOffset Now = Users.Now;

    private readonly INotificationDeliveryRepository _deliveries = Substitute.For<INotificationDeliveryRepository>();
    private readonly INotificationRepository _notifications = Substitute.For<INotificationRepository>();
    private readonly IPushDeviceRepository _devices = Substitute.For<IPushDeviceRepository>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IPushSender _push = Substitute.For<IPushSender>();
    private readonly IEmailSender _email = Substitute.For<IEmailSender>();
    private readonly INotificationMessageComposer _composer = Substitute.For<INotificationMessageComposer>();
    private readonly INotificationDeliverySettings _settings = Substitute.For<INotificationDeliverySettings>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly User _customer = Users.Customer();
    private readonly Notification _notification;

    public DeliverNotificationsHandlerTests()
    {
        _notification = Notification.Raise(_customer.Id, NotificationKind.YourBookingConfirmed, "Petra Rentals", Now, Id.New(), "KH-24-0007");
        _notifications.GetAsync(_notification.Id, Arg.Any<CancellationToken>()).Returns(_notification);
        _users.GetByIdAsync(_customer.Id, Arg.Any<CancellationToken>()).Returns(_customer);
        _push.IsConfigured.Returns(true);
        _composer.ComposePush(Arg.Any<Notification>(), Arg.Any<Language>()).Returns(new PushText("T", "B"));
        _composer.ComposeEmail(Arg.Any<Notification>(), Arg.Any<User>())
            .Returns(new EmailMessage("rana@example.jo", "Rana", "S", "<p>h</p>", "t"));
        _settings.MaxAttempts.Returns(3);
        _settings.BatchSize.Returns(10);
        _settings.Lease.Returns(TimeSpan.FromMinutes(2));
        _settings.RetryDelay(Arg.Any<int>()).Returns(TimeSpan.FromMinutes(1));
    }

    private NotificationDelivery Claim(NotificationChannel channel, int attempts = 1)
    {
        var delivery = NotificationDelivery.Owe(_notification.Id, channel, Now);
        for (var i = 0; i < attempts; i++)
            typeof(NotificationDelivery).GetProperty(nameof(NotificationDelivery.Attempts))!.SetValue(delivery, i + 1);
        return Claimed(delivery);
    }

    /// <summary>The claim hands the handler this row, still its own: the renewal matches and the row reads back.</summary>
    private NotificationDelivery Claimed(NotificationDelivery delivery)
    {
        _deliveries.ClaimDueAsync(Arg.Any<DateTimeOffset>(), Arg.Any<TimeSpan>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([new ClaimedNotificationDelivery(delivery.Id, delivery.Attempts)]);
        _deliveries.TryRenewClaimAsync(delivery.Id, delivery.Attempts, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _deliveries.GetAsync(delivery.Id, Arg.Any<CancellationToken>()).Returns(delivery);
        return delivery;
    }

    private PushDevice Device(string token, Language language) =>
        PushDevice.Register(token, PushPlatform.Android, _customer.Id, Guid.NewGuid(), language, null, Now);

    private Task<CSharpFunctionalExtensions.Result<DeliveryReport, Error>> RunAsync() =>
        new DeliverNotificationsHandler(
                _deliveries, _notifications, _devices, _users, _push, _email, _composer, _settings, _unitOfWork,
                new TestClock(Now), NullLogger<DeliverNotificationsHandler>.Instance)
            .Handle(new DeliverNotificationsCommand(), CancellationToken.None);

    [Fact]
    public async Task Every_live_phone_gets_it_in_its_own_language_and_the_row_is_sent()
    {
        var delivery = Claim(NotificationChannel.Push);
        _devices.ListDeliverableAsync(_customer.Id, Now, Arg.Any<CancellationToken>())
            .Returns([Device("a", Language.Arabic), Device("b", Language.English)]);
        _push.SendAsync(Arg.Any<PushMessage>(), Arg.Any<CancellationToken>()).Returns(PushSendResult.Delivered);

        var report = (await RunAsync()).Value;

        Assert.Same(DeliveryState.Sent, delivery.State);
        Assert.Equal(1, report.Sent);
        _composer.Received(1).ComposePush(_notification, Language.Arabic);
        _composer.Received(1).ComposePush(_notification, Language.English);
        await _push.Received(1).SendAsync(
            Arg.Is<PushMessage>(m => m.Token == "a"
                                     && m.Tag == _notification.Id.Value.ToString()
                                     && m.Data["kind"] == "YourBookingConfirmed"
                                     && m.Data["subjectId"] == _notification.SubjectId!.Value.Value.ToString()),
            Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Wave 4, W4-9: a notice about the account, not a booking, carries no subject at all. Every installed app opens a
    /// "subjectId" as a booking — a literal "null" would take a customer to /bookings/null — and prints a reference raw.
    /// </summary>
    [Fact]
    public async Task A_notice_about_the_account_wakes_the_phone_with_no_subject_and_no_reference()
    {
        var rejected = Notification.Raise(_customer.Id, NotificationKind.YourDocumentRejected, Notification.PlatformActorName, Now);
        _notifications.GetAsync(rejected.Id, Arg.Any<CancellationToken>()).Returns(rejected);
        Claimed(NotificationDelivery.Owe(rejected.Id, NotificationChannel.Push, Now));
        _devices.ListDeliverableAsync(_customer.Id, Now, Arg.Any<CancellationToken>()).Returns([Device("a", Language.English)]);
        _push.SendAsync(Arg.Any<PushMessage>(), Arg.Any<CancellationToken>()).Returns(PushSendResult.Delivered);

        await RunAsync();

        await _push.Received(1).SendAsync(
            Arg.Is<PushMessage>(m => m.Data["kind"] == "YourDocumentRejected"
                                     && !m.Data.ContainsKey("subjectId")
                                     && !m.Data.ContainsKey("subjectReference")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task No_provider_means_skipped_not_retried_forever()
    {
        var delivery = Claim(NotificationChannel.Push);
        _push.IsConfigured.Returns(false);

        await RunAsync();

        Assert.Same(DeliveryState.Skipped, delivery.State);
        await _push.DidNotReceiveWithAnyArgs().SendAsync(default!, default);
    }

    [Fact]
    public async Task Nobody_signed_in_means_skipped()
    {
        var delivery = Claim(NotificationChannel.Push);
        _devices.ListDeliverableAsync(_customer.Id, Now, Arg.Any<CancellationToken>()).Returns([]);

        await RunAsync();

        Assert.Same(DeliveryState.Skipped, delivery.State);
    }

    [Fact]
    public async Task A_dead_token_is_revoked_and_never_tried_again()
    {
        var delivery = Claim(NotificationChannel.Push);
        _devices.ListDeliverableAsync(_customer.Id, Now, Arg.Any<CancellationToken>()).Returns([Device("gone", Language.English)]);
        _push.SendAsync(Arg.Any<PushMessage>(), Arg.Any<CancellationToken>()).Returns(PushSendResult.DeadToken("UNREGISTERED"));

        await RunAsync();

        await _devices.Received(1).RevokeByTokenAsync("gone", Now, Arg.Any<CancellationToken>());
        Assert.Same(DeliveryState.Skipped, delivery.State);
    }

    [Fact]
    public async Task A_transient_failure_is_retried_and_given_up_at_the_limit()
    {
        var first = Claim(NotificationChannel.Push, attempts: 1);
        _devices.ListDeliverableAsync(_customer.Id, Now, Arg.Any<CancellationToken>()).Returns([Device("a", Language.English)]);
        _push.SendAsync(Arg.Any<PushMessage>(), Arg.Any<CancellationToken>()).Returns(PushSendResult.Transient("FCM 503"));

        await RunAsync();
        Assert.Same(DeliveryState.Pending, first.State);
        Assert.Equal(Now.AddMinutes(1), first.NextAttemptAt);

        var last = Claim(NotificationChannel.Push, attempts: 3);
        await RunAsync();
        Assert.Same(DeliveryState.Failed, last.State);
        Assert.Equal("FCM 503", last.LastError);
    }

    [Fact]
    public async Task One_phone_accepting_is_a_delivery_even_if_another_timed_out()
    {
        var delivery = Claim(NotificationChannel.Push);
        _devices.ListDeliverableAsync(_customer.Id, Now, Arg.Any<CancellationToken>())
            .Returns([Device("ok", Language.English), Device("slow", Language.English)]);
        _push.SendAsync(Arg.Is<PushMessage>(m => m.Token == "ok"), Arg.Any<CancellationToken>()).Returns(PushSendResult.Delivered);
        _push.SendAsync(Arg.Is<PushMessage>(m => m.Token == "slow"), Arg.Any<CancellationToken>()).Returns(PushSendResult.Transient("timeout"));

        await RunAsync();

        Assert.Same(DeliveryState.Sent, delivery.State);
    }

    [Fact]
    public async Task An_email_to_a_verified_address_is_sent()
    {
        var delivery = Claim(NotificationChannel.Email);

        await RunAsync();

        Assert.Same(DeliveryState.Sent, delivery.State);
        await _email.Received(1).SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_unverified_address_is_never_written_to()
    {
        var unverified = Build.Customer(emailVerified: false);
        var notification = Notification.Raise(unverified.Id, NotificationKind.YourBookingConfirmed, "Petra", Now, Id.New(), "KH-1");
        _notifications.GetAsync(notification.Id, Arg.Any<CancellationToken>()).Returns(notification);
        _users.GetByIdAsync(unverified.Id, Arg.Any<CancellationToken>()).Returns(unverified);
        var delivery = NotificationDelivery.Owe(notification.Id, NotificationChannel.Email, Now);
        Claimed(delivery);

        await RunAsync();

        Assert.Same(DeliveryState.Skipped, delivery.State);
        await _email.DidNotReceiveWithAnyArgs().SendAsync(default!, default);
    }

    [Fact]
    public async Task A_mail_transport_failure_is_retried()
    {
        var delivery = Claim(NotificationChannel.Email);
        _email.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns<EmailSendReceipt>(_ => throw new HttpRequestException("503"));

        await RunAsync();

        Assert.Same(DeliveryState.Pending, delivery.State);
        Assert.Equal("HttpRequestException", delivery.LastError);
    }

    // Pre-launch item 204: a row is worked only while its claim is still this pass's.

    [Fact]
    public async Task A_row_claimed_again_by_another_process_before_its_send_is_left_to_that_process()
    {
        var delivery = Claim(NotificationChannel.Email);
        _deliveries.TryRenewClaimAsync(delivery.Id, delivery.Attempts, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var report = (await RunAsync()).Value;

        Assert.Equal(1, report.TakenOver);
        Assert.Same(DeliveryState.Pending, delivery.State);
        await _email.DidNotReceiveWithAnyArgs().SendAsync(default!, default);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Fact]
    public async Task A_row_whose_claim_count_has_moved_is_not_sent()
    {
        var delivery = Claim(NotificationChannel.Push, attempts: 1);
        // Claimed again between the renewal and the read: the count the row carries is no longer this pass's.
        _deliveries.ClaimDueAsync(Arg.Any<DateTimeOffset>(), Arg.Any<TimeSpan>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([new ClaimedNotificationDelivery(delivery.Id, 0)]);
        _deliveries.TryRenewClaimAsync(delivery.Id, 0, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(true);

        var report = (await RunAsync()).Value;

        Assert.Equal(1, report.TakenOver);
        await _push.DidNotReceiveWithAnyArgs().SendAsync(default!, default);
    }

    [Fact]
    public async Task The_lease_is_renewed_from_the_moment_each_row_is_reached()
    {
        var delivery = Claim(NotificationChannel.Email);

        await RunAsync();

        await _deliveries.Received(1).TryRenewClaimAsync(delivery.Id, 1, Now.Add(TimeSpan.FromMinutes(2)), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_outcome_refused_because_the_claim_was_taken_over_during_the_send_does_not_stop_the_batch()
    {
        var first = NotificationDelivery.Owe(_notification.Id, NotificationChannel.Email, Now);
        var second = NotificationDelivery.Owe(_notification.Id, NotificationChannel.Push, Now);
        _deliveries.ClaimDueAsync(Arg.Any<DateTimeOffset>(), Arg.Any<TimeSpan>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([new ClaimedNotificationDelivery(first.Id, 0), new ClaimedNotificationDelivery(second.Id, 0)]);
        _deliveries.TryRenewClaimAsync(Arg.Any<Id>(), 0, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(true);
        _deliveries.GetAsync(first.Id, Arg.Any<CancellationToken>()).Returns(first);
        _deliveries.GetAsync(second.Id, Arg.Any<CancellationToken>()).Returns(second);
        _devices.ListDeliverableAsync(_customer.Id, Now, Arg.Any<CancellationToken>()).Returns([Device("a", Language.English)]);
        _push.SendAsync(Arg.Any<PushMessage>(), Arg.Any<CancellationToken>()).Returns(PushSendResult.Delivered);
        var saves = 0;
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(_ =>
            ++saves == 1 ? throw new ConcurrencyConflictException("taken over") : Task.FromResult(1));

        var report = (await RunAsync()).Value;

        Assert.Equal(1, report.TakenOver);
        Assert.Equal(1, report.Sent);
        await _push.Received(1).SendAsync(Arg.Any<PushMessage>(), Arg.Any<CancellationToken>());
        // The refused save's changes are forgotten before the next row, so they never ride into its save.
        _unitOfWork.Received(3).DiscardChanges();
    }

    [Fact]
    public void A_push_message_never_prints_its_token()
    {
        var message = new PushMessage("secret-token", "T", "B", new Dictionary<string, string>(), "tag");

        Assert.DoesNotContain("secret-token", message.ToString(), StringComparison.Ordinal);
    }
}
