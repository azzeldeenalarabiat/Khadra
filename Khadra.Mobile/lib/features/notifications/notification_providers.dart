import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/dtos.dart';
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

  while (true) {
    try {
      yield await api.unreadNotificationCount();
    } on Object {
      // A failed poll is not worth an error state on a badge. The next one will
      // either work or the screen itself will report the problem properly.
    }
    await Future<void>.delayed(const Duration(minutes: 1));
  }
});

final notificationsProvider =
    FutureProvider.autoDispose<NotificationFeed>((ref) async {
  final session = ref.watch(sessionProvider);
  if (!session.isSignedIn) return NotificationFeed.empty;
  return ref.watch(apiProvider).notifications(pageSize: 50);
});
