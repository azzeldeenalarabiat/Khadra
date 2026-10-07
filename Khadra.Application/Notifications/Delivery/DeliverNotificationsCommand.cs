using CSharpFunctionalExtensions;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess.Repositories;
using Khadra.Domain.Notifications;
using Khadra.Domain.Notifications.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Khadra.Application.Notifications.Delivery;

/// <summary>How the outbox is worked. Mechanics of a sender, not business rules.</summary>
public interface INotificationDeliverySettings
{
    /// <summary>How many rows one pass claims.</summary>
    int BatchSize { get; }

    /// <summary>How long a claimed row is held before another dispatcher may retry it.</summary>
    TimeSpan Lease { get; }

    /// <summary>How many claims before a delivery is given up as Failed.</summary>
    int MaxAttempts { get; }

    /// <summary>The wait before the retry that follows <paramref name="attempt"/>.</summary>
    TimeSpan RetryDelay(int attempt);
}

/// <param name="Claimed">Rows taken this pass.</param>
/// <param name="Sent">Accepted by the push service or the mail transport.</param>
/// <param name="Skipped">Nobody to send to: no live phone, no verified address, or no provider.</param>
/// <param name="Retrying">Failed this time and scheduled again.</param>
/// <param name="Failed">Given up.</param>
/// <param name="TakenOver">
/// Claimed again by another process after this one outlived its lease, and left to it (pre-launch item 204).
/// </param>
public sealed record DeliveryReport(int Claimed, int Sent, int Skipped, int Retrying, int Failed, int TakenOver = 0);

/// <summary>Works one batch of the notification outbox. Run by the dispatcher on a timer.</summary>
/// <remarks>
/// <b>One process per delivery</b> (pre-launch item 204). A batch can outlive its lease — a push service that stalls, a
/// slow database — and another process may then claim a row this one has yet to reach. So each row is worked only
/// while its claim count is still the one this pass's claim left: the lease is renewed for the send by a statement that
/// matches that count, and the count is a concurrency token, so an outcome saved after a takeover during the send is
/// refused by the database. Each row starts from an empty tracker, so one row's refused save never rides into the next.
/// </remarks>
public sealed record DeliverNotificationsCommand : ICommand<Result<DeliveryReport, Error>>;

public sealed partial class DeliverNotificationsHandler(
    INotificationDeliveryRepository deliveries,
    INotificationRepository notifications,
    IPushDeviceRepository devices,
    IUserRepository users,
    IPushSender push,
    IEmailSender email,
    INotificationMessageComposer composer,
    INotificationDeliverySettings settings,
    IUnitOfWork unitOfWork,
    IClock clock,
    ILogger<DeliverNotificationsHandler> logger)
    : IRequestHandler<DeliverNotificationsCommand, Result<DeliveryReport, Error>>
{
    public async Task<Result<DeliveryReport, Error>> Handle(
        DeliverNotificationsCommand request,
        CancellationToken cancellationToken)
    {
        var claimed = await deliveries.ClaimDueAsync(clock.UtcNow, settings.Lease, settings.BatchSize, cancellationToken);
        int sent = 0, skipped = 0, retrying = 0, failed = 0, takenOver = 0;

        foreach (var claim in claimed)
        {
            // Whatever an earlier row left in the tracker — above all a save the database refused — is not this row's.
            unitOfWork.DiscardChanges();

            NotificationDelivery? delivery;
            try
            {
                // Renewed from now, so the lease covers this row's send however long the rows before it took, and only
                // while the claim is still this pass's: a row claimed since by another process is that process's.
                if (!await deliveries.TryRenewClaimAsync(claim.DeliveryId, claim.Claims, clock.UtcNow.Add(settings.Lease), cancellationToken))
                {
                    takenOver++;
                    LogClaimTakenOver(logger, claim.DeliveryId.Value);
                    continue;
                }

                delivery = await deliveries.GetAsync(claim.DeliveryId, cancellationToken);
                if (delivery is null || delivery.State.IsFinal || delivery.Attempts != claim.Claims)
                {
                    takenOver++;
                    LogClaimTakenOver(logger, claim.DeliveryId.Value);
                    continue;
                }

                if (delivery.Channel == NotificationChannel.Push)
                    await DeliverPushAsync(delivery, cancellationToken);
                else
                    await DeliverEmailAsync(delivery, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
#pragma warning disable CA1031 // One bad row must not stop the batch; its lease makes it come back.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                LogDeliveryCrashed(logger, claim.DeliveryId.Value, exception);
                continue;
            }

            // The transport has answered, so its answer is recorded even while the process stops — unless the claim was
            // taken over during the send, which only a process frozen for longer than the lease can suffer. The row is
            // then the other process's, and it may send the same message again.
            try
            {
                await unitOfWork.SaveChangesAsync(CancellationToken.None);
            }
            catch (ConcurrencyConflictException)
            {
                takenOver++;
                LogOutcomeLost(logger, claim.DeliveryId.Value, delivery.Channel.Name);
                continue;
            }
#pragma warning disable CA1031 // One bad row must not stop the batch; its lease makes it come back.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                LogDeliveryCrashed(logger, claim.DeliveryId.Value, exception);
                continue;
            }

            if (delivery.State == DeliveryState.Sent) sent++;
            else if (delivery.State == DeliveryState.Skipped) skipped++;
            else if (delivery.State == DeliveryState.Failed) failed++;
            else retrying++;
        }

        unitOfWork.DiscardChanges();
        return new DeliveryReport(claimed.Count, sent, skipped, retrying, failed, takenOver);
    }

    private async Task DeliverPushAsync(NotificationDelivery delivery, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        if (!push.IsConfigured)
        {
            delivery.RecordSkipped("No push provider is configured.", now);
            return;
        }

        var notification = await notifications.GetAsync(delivery.NotificationId, cancellationToken);
        if (notification is null)
        {
            delivery.RecordSkipped("The notification no longer exists.", now);
            return;
        }

        var targets = await devices.ListDeliverableAsync(notification.RecipientUserId, now, cancellationToken);
        if (targets.Count == 0)
        {
            delivery.RecordSkipped("No phone is signed in for this person.", now);
            return;
        }

        var data = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["notificationId"] = notification.Id.Value.ToString(),
            ["kind"] = notification.Kind.Name,
        };
        if (notification.SubjectId is { } subject)
            data["subjectId"] = subject.Value.ToString();
        if (notification.SubjectReference is { } reference)
            data["subjectReference"] = reference;

        int accepted = 0, dead = 0;
        string? lastError = null;
        foreach (var device in targets)
        {
            var text = composer.ComposePush(notification, device.Language);
            var result = await push.SendAsync(
                new PushMessage(device.Token, text.Title, text.Body, data, notification.Id.Value.ToString()),
                cancellationToken);

            if (result.Accepted)
            {
                accepted++;
            }
            else if (result.TokenIsDead)
            {
                dead++;
                // The install is gone: uninstalled, or its data cleared. Nothing is ever sent to it again.
                await devices.RevokeByTokenAsync(device.Token, now, cancellationToken);
            }
            else
            {
                lastError = result.Error;
            }
        }

        // One phone that got it is a delivery. Retrying the whole message for a second phone that timed
        // out would re-send to the first, and the tag only hides that on Android.
        if (accepted > 0)
            delivery.RecordSent(now);
        else if (lastError is null)
            delivery.RecordSkipped($"Every phone's push token was dead ({dead}).", now);
        else
            delivery.RecordFailure(lastError, settings.MaxAttempts, now.Add(settings.RetryDelay(delivery.Attempts)), now);

        LogPushed(logger, notification.Kind.Name, targets.Count, accepted, dead, delivery.State.Name);
    }

    private async Task DeliverEmailAsync(NotificationDelivery delivery, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var notification = await notifications.GetAsync(delivery.NotificationId, cancellationToken);
        if (notification is null)
        {
            delivery.RecordSkipped("The notification no longer exists.", now);
            return;
        }

        var recipient = await users.GetByIdAsync(notification.RecipientUserId, cancellationToken);
        if (recipient is null || !recipient.IsEmailVerified)
        {
            // An unverified address is one nobody has proved they own. Same rule as the approval email.
            delivery.RecordSkipped("The recipient has no verified email address.", now);
            return;
        }

        try
        {
            await email.SendAsync(composer.ComposeEmail(notification, recipient), cancellationToken);
            delivery.RecordSent(now);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // A transport failure is a retry, whatever it threw.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            delivery.RecordFailure(
                exception.GetType().Name, settings.MaxAttempts, now.Add(settings.RetryDelay(delivery.Attempts)), now);
            LogEmailFailed(logger, notification.Kind.Name, delivery.Attempts, exception);
        }
    }

    [LoggerMessage(2300, LogLevel.Information,
        "Push for {Kind}: {Devices} phone(s), {Accepted} accepted, {Dead} dead token(s); delivery {State}.")]
    private static partial void LogPushed(ILogger logger, string kind, int devices, int accepted, int dead, string state);

    [LoggerMessage(2301, LogLevel.Warning, "Email for {Kind} was not accepted (attempt {Attempt}); it will be retried.")]
    private static partial void LogEmailFailed(ILogger logger, string kind, int attempt, Exception exception);

    [LoggerMessage(2302, LogLevel.Error, "Delivery {DeliveryId} crashed; its lease will bring it back.")]
    private static partial void LogDeliveryCrashed(ILogger logger, Guid deliveryId, Exception exception);

    [LoggerMessage(2303, LogLevel.Warning,
        "Delivery {DeliveryId} was claimed by another process after this one outlived its lease; it is left to that process.")]
    private static partial void LogClaimTakenOver(ILogger logger, Guid deliveryId);

    [LoggerMessage(2304, LogLevel.Error,
        "The outcome of delivery {DeliveryId} on {Channel} could not be recorded: another process claimed it during the send, after this one outlived its lease. That process may send it again.")]
    private static partial void LogOutcomeLost(ILogger logger, Guid deliveryId, string channel);
}
