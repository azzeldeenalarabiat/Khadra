import 'dart:async';

import 'package:flutter/material.dart';

import 'api/api_failure.dart';
import 'theme/khadra_theme.dart';
import 'widgets/khadra_widgets.dart';

/// A list that has loaded some of what the server holds, and knows there is more.
///
/// Every long list in this app is the same shape: page one arrives, later pages
/// are APPENDED, and a change of filter starts over. Three screens were writing
/// that out separately and two of them simply asked for fifty rows and stopped —
/// which is not a page size, it is a truncation nobody is told about.
///
/// `total` is the SERVER's count over the whole list, never `items.length`. A
/// screen saying "50 bookings" from the length of the page it happens to hold is
/// wrong the moment there are fifty-one.
@immutable
class PagedList<T> {
  const PagedList({
    required this.items,
    required this.page,
    required this.total,
    required this.hasMore,
    this.loadingMore = false,
  });

  const PagedList.empty()
    : items = const [],
      page = 1,
      total = 0,
      hasMore = false,
      loadingMore = false;

  final List<T> items;
  final int page;
  final int total;
  final bool hasMore;

  /// A page beyond the first is in flight. Distinct from the whole list loading:
  /// what is already on screen stays on screen, and only the footer spins.
  final bool loadingMore;

  bool get isEmpty => items.isEmpty;

  PagedList<T> working() => PagedList<T>(
    items: items,
    page: page,
    total: total,
    hasMore: hasMore,
    loadingMore: true,
  );

  PagedList<T> settled() =>
      PagedList<T>(items: items, page: page, total: total, hasMore: hasMore);
}

/// Loads the next page onto the end of the current one.
///
/// Shared because the failure branch is the part that is easy to get wrong: when
/// page three fails, the two pages already read are still perfectly good, and
/// replacing them with an error throws away what the customer is reading. The
/// error is rethrown so the CALLER can show it as a transient message instead.
typedef PageFetcher<T> =
    Future<PagedList<T>> Function(int page, List<T> existing);

Future<PagedList<T>?> loadNextPage<T>({
  required PagedList<T>? current,
  required void Function(PagedList<T>) emit,
  required PageFetcher<T> fetch,
}) async {
  if (current == null || !current.hasMore || current.loadingMore) return null;

  emit(current.working());
  try {
    final next = await fetch(current.page + 1, current.items);
    emit(next);
    return next;
  } on ApiFailure {
    emit(current.settled());
    rethrow;
  }
}

/// Fires [onReachEnd] when the list is scrolled near its bottom.
///
/// 600 logical pixels of lead time, which on a phone is roughly two cards: far
/// enough that the next page is usually there before the customer arrives at the
/// gap, close enough that idle scrolling does not pull the whole table down.
class EndOfListLoader {
  EndOfListLoader({required this.controller, required this.onReachEnd}) {
    controller.addListener(_check);
  }

  final ScrollController controller;
  final VoidCallback onReachEnd;

  static const _leadPixels = 600.0;

  void _check() {
    if (!controller.hasClients) return;
    final position = controller.position;
    if (position.pixels >= position.maxScrollExtent - _leadPixels) {
      onReachEnd();
    }
  }

  void dispose() => controller.removeListener(_check);
}

/// The foot of a paged list: a spinner while more is coming, then nothing.
///
/// There is deliberately no "load more" BUTTON. The list loads as it is scrolled,
/// and a button below a list that is already fetching invites a second request
/// for the same page.
class PagedListFooter extends StatelessWidget {
  const PagedListFooter({super.key, required this.list, this.endNote});

  final PagedList<Object?> list;

  /// A line to close the list with once everything has been read. Optional: most
  /// lists just stop.
  final String? endNote;

  @override
  Widget build(BuildContext context) {
    if (list.loadingMore) return const KhadraLoading(compact: true);
    if (list.hasMore || endNote == null) {
      return const SizedBox(height: Space.lg);
    }
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: Space.lg),
      child: Text(
        endNote!,
        textAlign: TextAlign.center,
        style: const TextStyle(color: KhadraColors.neutral500, fontSize: 12),
      ),
    );
  }
}
