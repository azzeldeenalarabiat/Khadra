import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../features/bookings/booking_providers.dart';
import '../../features/notifications/notification_providers.dart';
import 'live_refresh.dart';

/// Which tab is which, in the order `AppShell` lays them out.
abstract final class Tabs {
  static const home = 0;
  static const bookings = 1;
  static const alerts = 2;
  static const saved = 3;
  static const profile = 4;
}

/// Every live surface in the customer app, in one readable place.
///
/// The cadences are `docs/refresh-policy.md`, and each `visible` closure is the whole reason this
/// costs almost nothing: on a phone showing the catalogue, the only thing asking the server anything
/// is the badge.
///
/// Registered once by the shell, because these four are app-level — the bar and the two tabs that
/// must not go stale — rather than anything a single screen owns. A screen with its own live data
/// registers its own and unregisters on dispose.
void registerCustomerSurfaces(WidgetRef ref) {
  final live = ref.read(liveRefreshProvider);
  int tab() => ref.read(visibleTabProvider);

  live
    ..register(LiveSurface(
      id: 'bookings.list',
      poll: const Duration(seconds: 60),
      visible: () => tab() == Tabs.bookings,
      refresh: () {
        final selected = ref.read(selectedBookingTabProvider);
        return ref.read(myBookingsProvider(selected).notifier).refreshQuietly();
      },
    ))
    ..register(LiveSurface(
      id: 'bookings.counts',
      poll: const Duration(seconds: 60),
      visible: () => tab() == Tabs.bookings,
      refresh: () => ref.read(bookingTabCountsProvider.notifier).refreshQuietly(),
    ))
    // The landing card, and the most time-critical surface in the app: there is no push channel
    // yet, so a customer learns their booking was approved by opening this screen — and since
    // 2026-09-11 they have two hours to pay from that moment.
    ..register(LiveSurface(
      id: 'bookings.next',
      poll: const Duration(seconds: 60),
      visible: () => tab() == Tabs.home,
      refresh: () => ref.read(nextBookingProvider.notifier).refreshQuietly(),
    ))
    // No `visible`: the bar carrying this badge is on every tab, so by the policy's own rule it is
    // always the surface in front of somebody. It still stops dead when the app is not resumed.
    ..register(LiveSurface(
      id: 'notifications.unread',
      poll: const Duration(seconds: 60),
      refresh: () => ref.read(unreadNotificationCountProvider.notifier).refreshQuietly(),
    ));
}

/// Ids, so a caller cannot mistype one into silence.
abstract final class Surfaces {
  static const bookingsList = 'bookings.list';
  static const bookingsCounts = 'bookings.counts';
  static const nextBooking = 'bookings.next';
  static const unread = 'notifications.unread';

  static const all = <String>[bookingsList, bookingsCounts, nextBooking, unread];
}
