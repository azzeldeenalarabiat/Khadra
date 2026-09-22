import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/dtos.dart';
import '../../core/paging.dart';
import '../../core/providers.dart';

/// The badge on the alerts tab.
///
/// POLLED, because there is no push channel on this platform yet (pre-launch
/// checklist item 73). A customer learns their booking was approved by opening the
/// app — and since 2026-09-11 the deposit is owed within two hours of that
/// approval, which makes the missing channel a real cost rather than a
/// convenience (item 90).
///
/// A minute is deliberately unhurried: the count is a nudge, not a countdown, and
/// a phone on a Jordanian mobile network should not spend its battery on it.
/// The badge on the alerts tab.
///
/// It used to own its own `while` loop and a one-minute `Future.delayed`, which had no idea whether
/// anybody was looking at the phone — so a backgrounded app went on polling for as long as it sat
/// there. That is worse than wasted battery: every request runs `AuthInterceptor.onRequest`, which
/// rotates the refresh token when the access token is stale, so the app rotated every few minutes in
/// the background and each rotation is a chance to hit pre-launch items 126 and 128 and sign the
/// customer out of a session they never left.
///
/// `LiveRefresh` owns the schedule now, and stops it the moment the app is not `resumed`. The badge
/// still polls on every tab while the app IS in front, because the bar it sits in is on every tab.
class UnreadNotificationCountNotifier extends AsyncNotifier<int> {
  @override
  Future<int> build() async {
    final signedIn = ref.watch(sessionProvider.select((state) => state.isSignedIn));
    if (!signedIn) return 0;
    return ref.read(apiProvider).unreadNotificationCount();
  }

  Future<bool> refreshQuietly() async {
    if (!ref.read(sessionProvider).isSignedIn) return true;
    try {
      state = AsyncData(await ref.read(apiProvider).unreadNotificationCount());
      return true;
    } on Object {
      // A failed poll is not worth an error state on a badge. The number that was there stays.
      return false;
    }
  }
}

final unreadNotificationCountProvider =
    AsyncNotifierProvider<UnreadNotificationCountNotifier, int>(
  UnreadNotificationCountNotifier.new,
);

/// The alerts feed, a page at a time.
///
/// `unreadCount` rides along on the list rather than being counted from the rows
/// held: it is the server's figure over the WHOLE feed, and the "mark all read"
/// action has to appear for somebody whose unread alert is on page four.
class NotificationsNotifier
    extends AutoDisposeAsyncNotifier<PagedList<NotificationItem>> {
  static const _pageSize = 25;

  int _unread = 0;
  int get unreadCount => _unread;

  @override
  Future<PagedList<NotificationItem>> build() async {
    final session = ref.watch(sessionProvider);
    if (!session.isSignedIn) {
      _unread = 0;
      return const PagedList<NotificationItem>.empty();
    }
    return _fetch(page: 1, existing: const []);
  }

  Future<void> loadMore() async {
    await loadNextPage<NotificationItem>(
      current: state.valueOrNull,
      emit: (next) => state = AsyncData(next),
      fetch: (page, existing) => _fetch(page: page, existing: existing),
    );
  }

  Future<PagedList<NotificationItem>> _fetch({
    required int page,
    required List<NotificationItem> existing,
  }) async {
    final feed =
        await ref.read(apiProvider).notifications(page: page, pageSize: _pageSize);
    _unread = feed.unreadCount;

    return PagedList<NotificationItem>(
      items: [...existing, ...feed.items],
      page: feed.page,
      total: feed.totalCount,
      hasMore: feed.hasNext,
    );
  }
}

final notificationsProvider = AsyncNotifierProvider.autoDispose<
    NotificationsNotifier, PagedList<NotificationItem>>(
  NotificationsNotifier.new,
);
