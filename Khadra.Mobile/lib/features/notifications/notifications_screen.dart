import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../api/dtos.dart';
import '../../core/api/api_failure.dart';
import '../../core/api/api_failure_messages.dart';
import '../../core/format/booking_presentation.dart';
import '../../core/paging.dart';
import '../../core/providers.dart';
import '../../core/router.dart';
import '../../core/theme/khadra_theme.dart';
import '../../core/widgets/khadra_widgets.dart';
import '../../l10n/app_localizations.dart';
import '../auth/account_required.dart';
import 'notification_providers.dart';

class NotificationsScreen extends ConsumerStatefulWidget {
  const NotificationsScreen({super.key});

  @override
  ConsumerState<NotificationsScreen> createState() =>
      _NotificationsScreenState();
}

class _NotificationsScreenState extends ConsumerState<NotificationsScreen> {
  final _scrollController = ScrollController();
  late final EndOfListLoader _loader = EndOfListLoader(
    controller: _scrollController,
    onReachEnd: () => unawaited(_loadMore()),
  );

  @override
  void initState() {
    super.initState();
    _loader; // Attaches the listener.
  }

  @override
  void dispose() {
    _loader.dispose();
    _scrollController.dispose();
    super.dispose();
  }

  Future<void> _loadMore() async {
    try {
      await ref.read(notificationsProvider.notifier).loadMore();
    } on ApiFailure catch (failure) {
      if (!mounted) return;
      showKhadraMessage(
        context,
        failure.messageFor(AppLocalizations.of(context)),
        isError: true,
      );
    }
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final session = ref.watch(sessionProvider);

    if (!session.isSignedIn) {
      return Scaffold(
        appBar: AppBar(title: KhadraLargeTitle(l10n.notificationsTitle)),
        body: AccountRequired(
          icon: Icons.notifications_off_outlined,
          title: l10n.notificationsSignedOutTitle,
          next: Routes.notifications,
        ),
      );
    }

    final feed = ref.watch(notificationsProvider);
    // The server's count over the WHOLE feed, so the action appears for somebody
    // whose only unread alert is four pages down.
    final unread = ref.watch(notificationsProvider.notifier).unreadCount;

    return Scaffold(
      appBar: AppBar(
        title: KhadraLargeTitle(l10n.notificationsTitle),
        actions: [
          if (feed.hasValue && unread > 0)
            TextButton(
              onPressed: _markAllRead,
              child: Text(l10n.notificationsMarkAllRead),
            ),
        ],
      ),
      body: RefreshIndicator(
        onRefresh: () async {
          ref.invalidate(unreadNotificationCountProvider);
          ref.invalidate(notificationsProvider);
          await ref.read(notificationsProvider.future);
        },
        child: switch (feed) {
          AsyncLoading() => const KhadraLoading(),
          AsyncError(:final error) => KhadraError(
              message: ApiFailure.from(error).messageFor(l10n),
              onRetry: () => ref.invalidate(notificationsProvider),
            ),
          AsyncData(:final value) when value.isEmpty => ListView(
              children: [
                SizedBox(
                  height: MediaQuery.of(context).size.height * 0.55,
                  child: KhadraEmpty(
                    icon: Icons.notifications_none,
                    title: l10n.notificationsEmptyTitle,
                    body: l10n.notificationsEmptyBody,
                  ),
                ),
              ],
            ),
          AsyncData(:final value) => ListView.separated(
              controller: _scrollController,
              padding: const EdgeInsets.only(bottom: Space.bottomInset),
              itemCount: value.items.length + 1,
              separatorBuilder: (_, __) => const Divider(height: 1, indent: 64),
              itemBuilder: (_, index) => index == value.items.length
                  ? PagedListFooter(list: value)
                  : _NotificationRow(item: value.items[index]),
            ),
          _ => const KhadraLoading(),
        },
      ),
    );
  }

  Future<void> _markAllRead() async {
    final l10n = AppLocalizations.of(context);
    try {
      await ref.read(apiProvider).markAllNotificationsRead();
      ref.invalidate(notificationsProvider);
      ref.invalidate(unreadNotificationCountProvider);
    } on ApiFailure catch (failure) {
      if (mounted) {
        showKhadraMessage(context, failure.messageFor(l10n), isError: true);
      }
    }
  }
}

class _NotificationRow extends ConsumerWidget {
  const _NotificationRow({required this.item});

  final NotificationItem item;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);

    return ListTile(
      contentPadding: const EdgeInsets.symmetric(
          horizontal: Space.lg, vertical: Space.sm),
      leading: CircleAvatar(
        backgroundColor: item.isRead
            ? KhadraColors.neutral100
            : KhadraColors.accent100,
        child: Icon(
          _icon(item.kind),
          size: 20,
          color: item.isRead ? KhadraColors.neutral500 : KhadraColors.accent,
        ),
      ),
      title: Text(
        _message(l10n, item),
        style: TextStyle(
          fontSize: 14,
          height: 1.4,
          fontWeight: item.isRead ? FontWeight.w400 : FontWeight.w600,
        ),
      ),
      subtitle: Padding(
        padding: const EdgeInsets.only(top: 3),
        child: Row(
          children: [
            Text(
              BookingPresentation.relative(l10n, item.occurredAt),
              style: const TextStyle(
                  color: KhadraColors.neutral500, fontSize: 12),
            ),
            if (item.subjectReference != null) ...[
              const SizedBox(width: Space.sm),
              Flexible(
                child: LatinRun(
                  item.subjectReference!,
                  style: const TextStyle(
                      color: KhadraColors.neutral500, fontSize: 12),
                ),
              ),
            ],
          ],
        ),
      ),
      trailing: item.isRead
          ? null
          : const Icon(Icons.circle, size: 9, color: KhadraColors.accent),
      onTap: () => _open(context, ref),
    );
  }

  Future<void> _open(BuildContext context, WidgetRef ref) async {
    if (!item.isRead) {
      try {
        await ref.read(apiProvider).markNotificationRead(item.notificationId);
        ref.invalidate(notificationsProvider);
        ref.invalidate(unreadNotificationCountProvider);
      } on ApiFailure {
        // Failing to mark it read must not stop the customer opening what it is
        // about; the row simply stays bold.
      }
    }

    // Every customer-facing kind is about a booking, and `subjectId` is that
    // booking's id — except a dispute update, whose subject is the TICKET.
    if (item.subjectId != null && context.mounted) {
      context.push(item.kind == 'YourDisputeUpdated'
          ? Routes.dispute(item.subjectId!)
          : Routes.booking(item.subjectId!));
    }
  }

  /// The sentence, chosen from the KIND.
  ///
  /// A kind this build has never seen falls through to a generic line rather than
  /// rendering an empty row: the platform can add one at any time, and an old app
  /// in a store should degrade rather than break.
  static String _message(AppLocalizations l10n, NotificationItem item) =>
      switch (item.kind) {
        'YourBookingApproved' =>
          l10n.notificationYourBookingApproved(item.actorName),
        'YourBookingRejected' =>
          l10n.notificationYourBookingRejected(item.actorName),
        'YourBookingExpired' =>
          l10n.notificationYourBookingExpired(item.actorName),
        'YourBookingCompleted' =>
          l10n.notificationYourBookingCompleted(item.actorName),
        'YourBookingMarkedNoShow' =>
          l10n.notificationYourBookingMarkedNoShow(item.actorName),
        'YourBookingConfirmed' =>
          l10n.notificationYourBookingConfirmed(item.actorName),
        'YourBookingCancelled' =>
          l10n.notificationYourBookingCancelled(item.actorName),
        'YourBookingPickedUp' =>
          l10n.notificationYourBookingPickedUp(item.actorName),
        'YourBookingReturned' =>
          l10n.notificationYourBookingReturned(item.actorName),
        'YourPaymentReminder' => l10n.notificationYourPaymentReminder,
        'YourPickupReminder' =>
          l10n.notificationYourPickupReminder(item.actorName),
        'YourReturnReminder' =>
          l10n.notificationYourReturnReminder(item.actorName),
        'YourDisputeUpdated' => l10n.notificationYourDisputeUpdated,
        'YourDepositRefunded' =>
          l10n.notificationYourDepositRefunded(item.actorName),
        _ => l10n.notificationUnknown(item.actorName),
      };

  static IconData _icon(String kind) => switch (kind) {
        'YourBookingApproved' => Icons.check_circle_outline,
        'YourBookingRejected' => Icons.do_not_disturb_on_outlined,
        'YourBookingExpired' => Icons.timer_off_outlined,
        'YourBookingCompleted' => Icons.verified_outlined,
        'YourBookingMarkedNoShow' => Icons.person_off_outlined,
        'YourBookingConfirmed' => Icons.verified_user_outlined,
        'YourBookingCancelled' => Icons.event_busy_outlined,
        'YourBookingPickedUp' => Icons.key_outlined,
        'YourBookingReturned' => Icons.assignment_return_outlined,
        'YourPaymentReminder' => Icons.payments_outlined,
        'YourPickupReminder' || 'YourReturnReminder' => Icons.alarm_outlined,
        'YourDisputeUpdated' => Icons.gavel_outlined,
        'YourDepositRefunded' => Icons.currency_exchange_outlined,
        _ => Icons.notifications_none,
      };
}
