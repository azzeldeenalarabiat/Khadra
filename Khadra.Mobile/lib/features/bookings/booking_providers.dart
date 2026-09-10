import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/dtos.dart';
import '../../core/paging.dart';
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

/// The caller's bookings for one tab, a page at a time.
///
/// It used to ask for fifty and stop. Fifty is not a page size, it is a silent
/// truncation: a customer's booking HISTORY is the one list on this app that
/// only grows, and the fifty-first rental simply did not exist as far as the
/// screen was concerned.
class MyBookingsNotifier
    extends AutoDisposeFamilyAsyncNotifier<PagedList<BookingListItem>, String> {
  static const _pageSize = 20;

  @override
  Future<PagedList<BookingListItem>> build(String tab) async {
    final session = ref.watch(sessionProvider);
    if (!session.isSignedIn) return const PagedList<BookingListItem>.empty();
    return _fetch(tab, page: 1, existing: const []);
  }

  Future<void> loadMore() async {
    await loadNextPage<BookingListItem>(
      current: state.valueOrNull,
      emit: (next) => state = AsyncData(next),
      fetch: (page, existing) => _fetch(arg, page: page, existing: existing),
    );
  }

  Future<PagedList<BookingListItem>> _fetch(
    String tab, {
    required int page,
    required List<BookingListItem> existing,
  }) async {
    final result = await ref
        .read(apiProvider)
        .myBookings(tab: tab, page: page, pageSize: _pageSize);

    return PagedList<BookingListItem>(
      items: [...existing, ...result.items],
      page: result.page,
      total: result.totalCount,
      hasMore: result.hasNext,
    );
  }
}

final myBookingsProvider = AsyncNotifierProvider.autoDispose
    .family<MyBookingsNotifier, PagedList<BookingListItem>, String>(
  MyBookingsNotifier.new,
);

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
