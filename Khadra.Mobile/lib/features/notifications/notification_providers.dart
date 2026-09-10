import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/dtos.dart';
import '../../core/paging.dart';
import '../../core/providers.dart';

/// The badge on the alerts tab.
///
/// POLLED, because there is no push channel on this platform yet (pre-launch
/// checklist item 73). A customer learns their booking was approved by opening the
/// app, which is also why the deposit payment window is 24 hours rather than the
/// one hour first proposed.
///
/// A minute is deliberately unhurried: the count is a nudge, not a countdown, and
/// a phone on a Jordanian mobile network should not spend its battery on it.
final unreadNotificationCountProvider = StreamProvider<int>((ref) async* {
  final session = ref.watch(sessionProvider);
  if (!session.isSignedIn) {
    yield 0;
    return;
  }

  final api = ref.watch(apiProvider);

  // Set when this provider is torn down. Without it a poll parked on its minute
  // would wake up after a sign-out and fire one last request carrying no token --
  // refused by the server, and read by the interceptor as an expired session.
  var disposed = false;
  ref.onDispose(() => disposed = true);

  while (!disposed) {
    try {
      yield await api.unreadNotificationCount();
    } on Object {
      // A failed poll is not worth an error state on a badge. The next one will
      // either work or the screen itself will report the problem properly.
    }
    await Future<void>.delayed(const Duration(minutes: 1));
  }
});

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
