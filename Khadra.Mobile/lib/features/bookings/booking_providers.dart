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
    // `select`, not the whole state. `SessionState` has no value equality, so every token rotation
    // — one every few minutes — assigned a new object and re-ran this build, re-reading the list and
    // the counts for a session that had not changed in any way a screen can see. Watching the one
    // fact this cares about makes a rotation cost nothing.
    final signedIn = ref.watch(sessionProvider.select((state) => state.isSignedIn));
    if (!signedIn) return const PagedList<BookingListItem>.empty();
    return _fetch(tab, page: 1, existing: const []);
  }

  /// Re-reads the list WITHOUT taking it away if the read fails.
  ///
  /// `ref.invalidate` is the loud path: it drops the state, so a failed reload leaves the screen
  /// showing an error instead of the rows the customer was reading. That is right for a
  /// pull-to-refresh and wrong for a poll on a Jordanian mobile network, which will fail routinely.
  ///
  /// Returns whether the answer arrived, which is what the refresh policy measures its floor and its
  /// backoff against.
  Future<bool> refreshQuietly() async {
    // A guest asks the server nothing. `build` above already answers empty for them, but a poll does
    // not go through `build` — and `account_required_test` COUNTS calls rather than trusting that.
    if (!ref.read(sessionProvider).isSignedIn) return true;
    try {
      state = AsyncData(await _fetch(arg, page: 1, existing: const []));
      return true;
    } on Object {
      return false;
    }
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
/// A notifier rather than a `FutureProvider`, so it can be re-read WITHOUT being emptied.
///
/// A bare `FutureProvider` can only be invalidated, and invalidating drops the value — so a failed
/// background poll would blank every tab count to nothing and put them back a minute later.
class BookingTabCountsNotifier extends AutoDisposeAsyncNotifier<Map<String, int>> {
  @override
  Future<Map<String, int>> build() async {
    final signedIn = ref.watch(sessionProvider.select((state) => state.isSignedIn));
    if (!signedIn) return const {};
    return ref.read(apiProvider).bookingTabCounts();
  }

  Future<bool> refreshQuietly() async {
    if (!ref.read(sessionProvider).isSignedIn) return true;
    try {
      state = AsyncData(await ref.read(apiProvider).bookingTabCounts());
      return true;
    } on Object {
      return false;
    }
  }
}

final bookingTabCountsProvider =
    AsyncNotifierProvider.autoDispose<BookingTabCountsNotifier, Map<String, int>>(
  BookingTabCountsNotifier.new,
);

/// The one booking the landing surface shows, or null.
///
/// **Which booking is "next" is the server's answer, not this app's.** A deposit
/// due within hours outranks a rental starting tomorrow, which outranks an
/// unanswered request; a screen scanning `myBookings` for the earliest pickup
/// would have shown the rental and let the deposit expire unread.
///
/// It matters because there is no push channel yet: a customer learns their
/// booking was approved by opening the app, and since 2026-09-11 they have two
/// hours to pay rather than a day. The first screen they land on is the only
/// thing that can tell them in time.
/// A notifier for the same reason the tab counts are one: it is polled, so it must be re-readable
/// without being emptied. It is also the most time-critical surface in the app — see above.
class NextBookingNotifier extends AutoDisposeAsyncNotifier<NextBooking?> {
  @override
  Future<NextBooking?> build() async {
    final signedIn = ref.watch(sessionProvider.select((state) => state.isSignedIn));
    if (!signedIn) return null;
    return ref.read(apiProvider).nextBooking();
  }

  Future<bool> refreshQuietly() async {
    if (!ref.read(sessionProvider).isSignedIn) return true;
    try {
      state = AsyncData(await ref.read(apiProvider).nextBooking());
      return true;
    } on Object {
      return false;
    }
  }
}

final nextBookingProvider =
    AsyncNotifierProvider.autoDispose<NextBookingNotifier, NextBooking?>(
  NextBookingNotifier.new,
);

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

/// What galleries are told about the caller.
///
/// `autoDispose`, and asked for only by the screen that shows it. The reader
/// behind it walks every finished booking this customer has, which is fine on a
/// page opened a few times a year and would be waste on a tab opened daily —
/// which is also why there is no badge for it on the profile row.
final myReputationProvider =
    FutureProvider.autoDispose<CustomerReputation>((ref) async {
  return ref.watch(apiProvider).myReputation();
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
  // The landing card too. A booking cancelled on its detail screen must not be
  // sitting on the browse tab as "your next rental" when the customer gets back
  // to it — which is exactly the tab they return to.
  ref.invalidate(nextBookingProvider);
  for (final tab in BookingTabs.ordered) {
    ref.invalidate(myBookingsProvider(tab));
  }
}
