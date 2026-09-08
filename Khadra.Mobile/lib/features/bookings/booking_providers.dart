import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/dtos.dart';
import '../../core/providers.dart';

/// The tabs the platform defines, in the order a customer reads them.
///
/// These are the SERVER's vocabulary — `BookingTabs` in the application layer —
/// and the app sends the name rather than a status list of its own. That is what
/// stops the app and the server disagreeing about which bookings are "upcoming"
/// the day the platform adds a state.
abstract final class BookingTabs {
  static const all = 'all';
  static const pending = 'pending';
  static const upcoming = 'upcoming';
  static const active = 'active';
  static const returned = 'returned';
  static const completed = 'completed';
  static const closed = 'closed';
  static const disputed = 'disputed';

  static const ordered = <String>[
    all,
    pending,
    upcoming,
    active,
    returned,
    completed,
    closed,
    disputed,
  ];
}

final selectedBookingTabProvider =
    StateProvider<String>((ref) => BookingTabs.all);

/// One page of the caller's bookings for one tab.
final myBookingsProvider = FutureProvider.autoDispose
    .family<Paged<BookingListItem>, String>((ref, tab) async {
  final session = ref.watch(sessionProvider);
  if (!session.isSignedIn) return Paged.empty();
  return ref.watch(apiProvider).myBookings(tab: tab, pageSize: 50);
});

/// How many bookings sit behind each tab.
///
/// The database's answer, not a count of what one page happened to return: a tab
/// showing "3" from a page of twenty would be wrong the moment there were more.
final bookingTabCountsProvider =
    FutureProvider.autoDispose<Map<String, int>>((ref) async {
  final session = ref.watch(sessionProvider);
  if (!session.isSignedIn) return const {};
  return ref.watch(apiProvider).bookingTabCounts();
});

/// One booking in full.
final bookingProvider =
    FutureProvider.autoDispose.family<Booking, String>((ref, bookingId) async {
  return ref.watch(apiProvider).booking(bookingId);
});

/// The caller's own review of a booking, or null if they have not left one.
///
/// Null is a legitimate answer about a booking that exists, which is why the API
/// returns 200 with nothing rather than a 404 — the screen needs to tell "not
/// reviewed yet" from "not your booking" to decide whether to offer the form.
final myReviewProvider =
    FutureProvider.autoDispose.family<MyReview?, String>((ref, bookingId) async {
  return ref.watch(apiProvider).myReview(bookingId);
});

final disputeProvider =
    FutureProvider.autoDispose.family<Dispute, String>((ref, ticketId) async {
  return ref.watch(apiProvider).dispute(ticketId);
});

/// Re-reads everything a booking action could have changed.
///
/// Called after a cancel, a review or a dispute. Invalidating the list and the
/// counts as well as the booking is the point: a cancelled booking moves between
/// tabs, and a tab count that did not follow it would be visibly wrong.
void invalidateBookings(WidgetRef ref, {String? bookingId}) {
  if (bookingId != null) {
    ref.invalidate(bookingProvider(bookingId));
    ref.invalidate(myReviewProvider(bookingId));
  }
  ref.invalidate(bookingTabCountsProvider);
  for (final tab in BookingTabs.ordered) {
    ref.invalidate(myBookingsProvider(tab));
  }
}
