import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../api/dtos.dart';
import '../../core/api/api_failure.dart';
import '../../core/api/api_failure_messages.dart';
import '../../core/format/booking_presentation.dart';
import '../../core/format/formats.dart';
import '../../core/paging.dart';
import '../../core/providers.dart';
import '../../core/router.dart';
import '../../core/theme/khadra_theme.dart';
import '../../core/widgets/khadra_widgets.dart';
import '../../l10n/app_localizations.dart';
import 'booking_providers.dart';

class BookingsScreen extends ConsumerStatefulWidget {
  const BookingsScreen({super.key});

  @override
  ConsumerState<BookingsScreen> createState() => _BookingsScreenState();
}

class _BookingsScreenState extends ConsumerState<BookingsScreen> {
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
    final tab = ref.read(selectedBookingTabProvider);
    try {
      await ref.read(myBookingsProvider(tab).notifier).loadMore();
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
        appBar: AppBar(title: KhadraLargeTitle(l10n.bookingsTitle)),
        body: KhadraEmpty(
          icon: Icons.lock_outline,
          title: l10n.bookingsSignedOutTitle,
          body: l10n.bookingsSignedOutBody,
          action: FilledButton(
            onPressed: () => context.push(Routes.signIn),
            child: Text(l10n.authSignIn),
          ),
        ),
      );
    }

    final tab = ref.watch(selectedBookingTabProvider);
    final counts = ref.watch(bookingTabCountsProvider).valueOrNull ?? const {};
    final bookings = ref.watch(myBookingsProvider(tab));
    final formats = ref.watch(formatsProvider);

    return Scaffold(
      appBar: AppBar(
        title: KhadraLargeTitle(l10n.bookingsTitle),
        bottom: PreferredSize(
          preferredSize: const Size.fromHeight(52),
          child: SizedBox(
            height: 52,
            child: ListView(
              scrollDirection: Axis.horizontal,
              padding: const EdgeInsets.symmetric(horizontal: Space.lg),
              children: [
                for (final name in BookingTabs.ordered)
                  Padding(
                    padding: const EdgeInsetsDirectional.only(end: Space.sm),
                    child: _TabChip(
                      label: _tabLabel(l10n, name),
                      // The COUNT is the database's, over the whole tab, not the
                      // length of the page that happens to be loaded.
                      count: counts[name] ?? 0,
                      selected: tab == name,
                      onTap: () => ref
                          .read(selectedBookingTabProvider.notifier)
                          .state = name,
                    ),
                  ),
              ],
            ),
          ),
        ),
      ),
      body: RefreshIndicator(
        onRefresh: () async {
          invalidateBookings(ref);
          await ref.read(myBookingsProvider(tab).future);
        },
        child: switch (bookings) {
          AsyncLoading() => const KhadraLoading(),
          AsyncError(:final error) => KhadraError(
              message: ApiFailure.from(error).messageFor(l10n),
              onRetry: () => ref.invalidate(myBookingsProvider(tab)),
            ),
          AsyncData(:final value) when value.isEmpty =>
            _empty(l10n, tab, counts),
          AsyncData(:final value) when formats != null => ListView.separated(
              controller: _scrollController,
              padding: const EdgeInsets.fromLTRB(
                  Space.lg, Space.lg, Space.lg, Space.bottomInset),
              // One extra row for the footer, which is where the next page's
              // spinner lives.
              itemCount: value.items.length + 1,
              separatorBuilder: (_, __) => const SizedBox(height: Space.md),
              itemBuilder: (_, index) => index == value.items.length
                  ? PagedListFooter(list: value)
                  : _BookingRow(booking: value.items[index], formats: formats),
            ),
          _ => const KhadraLoading(),
        },
      ),
    );
  }

  /// Nothing here — but WHICH nothing.
  ///
  /// "You have no bookings yet, go and find a car" is the right thing to say to
  /// somebody who has never booked. It is the wrong thing to say to somebody with
  /// four live rentals who has tapped Disputed, and it was said to both.
  Widget _empty(AppLocalizations l10n, String tab, Map<String, int> counts) {
    // The database's own count over EVERY tab, not the length of this one.
    final hasAnyBooking = (counts[BookingTabs.all] ?? 0) > 0;
    final filtered = tab != BookingTabs.all && hasAnyBooking;

    return ListView(
      // Inside a scroll view so pull-to-refresh still works on an empty list,
      // which is exactly when somebody is most likely to try it.
      children: [
        SizedBox(
          height: MediaQuery.of(context).size.height * 0.55,
          child: filtered
              ? KhadraEmpty(
                  icon: Icons.filter_list_off_outlined,
                  title: l10n.bookingsEmptyTabTitle,
                  body: l10n.bookingsEmptyTabBody,
                  action: OutlinedButton(
                    onPressed: () => ref
                        .read(selectedBookingTabProvider.notifier)
                        .state = BookingTabs.all,
                    child: Text(l10n.bookingsTabAll),
                  ),
                )
              : KhadraEmpty(
                  icon: Icons.event_note_outlined,
                  title: l10n.bookingsEmptyTitle,
                  body: l10n.bookingsEmptyBody,
                  action: FilledButton(
                    onPressed: () => context.go(Routes.search),
                    child: Text(l10n.bookingsEmptyAction),
                  ),
                ),
        ),
      ],
    );
  }

  String _tabLabel(AppLocalizations l10n, String tab) => switch (tab) {
        BookingTabs.all => l10n.bookingsTabAll,
        BookingTabs.pending => l10n.bookingsTabPending,
        BookingTabs.upcoming => l10n.bookingsTabUpcoming,
        BookingTabs.active => l10n.bookingsTabActive,
        BookingTabs.returned => l10n.bookingsTabReturned,
        BookingTabs.completed => l10n.bookingsTabCompleted,
        BookingTabs.closed => l10n.bookingsTabClosed,
        BookingTabs.disputed => l10n.bookingsTabDisputed,
        _ => tab,
      };
}

class _TabChip extends StatelessWidget {
  const _TabChip({
    required this.label,
    required this.count,
    required this.selected,
    required this.onTap,
  });

  final String label;
  final int count;
  final bool selected;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) => Material(
        color: selected ? KhadraColors.accent : KhadraColors.surface,
        borderRadius: Radii.chip,
        child: InkWell(
          onTap: onTap,
          borderRadius: Radii.chip,
          child: Container(
            padding: const EdgeInsets.symmetric(
                horizontal: Space.lg, vertical: Space.sm),
            decoration: BoxDecoration(
              borderRadius: Radii.chip,
              border: Border.all(
                color: selected ? KhadraColors.accent : KhadraColors.neutral300,
              ),
            ),
            child: Row(
              mainAxisSize: MainAxisSize.min,
              children: [
                Text(
                  label,
                  style: TextStyle(
                    fontSize: 13,
                    fontWeight: FontWeight.w600,
                    color: selected ? Colors.white : KhadraColors.neutral700,
                  ),
                ),
                if (count > 0) ...[
                  const SizedBox(width: 6),
                  Text(
                    count.toString(),
                    style: TextStyle(
                      fontSize: 12,
                      fontWeight: FontWeight.w700,
                      color: selected
                          ? Colors.white.withValues(alpha: 0.85)
                          : KhadraColors.neutral500,
                    ),
                  ),
                ],
              ],
            ),
          ),
        ),
      );
}

class _BookingRow extends StatelessWidget {
  const _BookingRow({required this.booking, required this.formats});

  final BookingListItem booking;
  final Formats formats;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);

    return KhadraCard(
      padding: const EdgeInsets.all(Space.md),
      onTap: () => context.push(Routes.booking(booking.bookingId)),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              SizedBox(
                width: 92,
                height: 74,
                child: KhadraImage(
                  url: booking.vehicle?.coverImageUrl,
                  borderRadius: Radii.field,
                ),
              ),
              const SizedBox(width: Space.md),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    // The car and its state on ONE line, which is the design's
                    // shape and also the order somebody reads in: which car, and
                    // what is happening to it.
                    Row(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Expanded(
                          child: Text(
                            // The car can be null: a booking is a financial record
                            // that outlives the listing behind it.
                            booking.vehicle?.title ?? booking.dealerName,
                            style: const TextStyle(
                                fontSize: 14, fontWeight: FontWeight.w800),
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                          ),
                        ),
                        const SizedBox(width: Space.sm),
                        KhadraBadge(
                          label: BookingPresentation.label(l10n, booking.status),
                          colour: BookingPresentation.colour(booking.status),
                        ),
                      ],
                    ),
                    const SizedBox(height: 3),
                    Text(
                      booking.dealerName,
                      style: const TextStyle(
                        color: KhadraColors.neutral600,
                        fontSize: 11,
                        fontWeight: FontWeight.w600,
                      ),
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                    ),
                    const SizedBox(height: 6),
                    Text(
                      // The DAYS figure is frozen on the booking. Recomputing it
                      // from the two instants would answer a different question
                      // from the one the invoice was written against.
                      '${formats.dateRange(booking.periodStart, booking.periodEnd)} · ${l10n.bookDays(booking.days)}',
                      style: const TextStyle(
                        color: KhadraColors.neutral800,
                        fontSize: 12,
                        fontWeight: FontWeight.w600,
                      ),
                    ),
                    const SizedBox(height: 5),
                    Text(
                      formats.moneyOf(booking.totalPrice, booking.currency),
                      style: const TextStyle(
                        fontSize: 13,
                        fontWeight: FontWeight.w700,
                        color: KhadraColors.price,
                      ),
                    ),
                    if (booking.hasLiveDispute) ...[
                      const SizedBox(height: 6),
                      KhadraBadge(
                        label: l10n.bookingsTabDisputed,
                        colour: KhadraColors.bad,
                        icon: Icons.gavel_outlined,
                      ),
                    ],
                  ],
                ),
              ),
            ],
          ),
          const SizedBox(height: 11),
          const Divider(height: 1),
          const SizedBox(height: 11),
          // The reference and the way in, on one line. The reference is what a
          // customer and a gallery say to each other on the phone; the link is
          // what the design puts opposite it so the card says where it goes.
          Row(
            children: [
              Expanded(
                child: LatinRun(
                  booking.reference,
                  style: const TextStyle(
                    fontSize: 11,
                    fontWeight: FontWeight.w600,
                    color: KhadraColors.neutral500,
                    letterSpacing: 0.4,
                  ),
                ),
              ),
              const SizedBox(width: Space.sm),
              Text(
                l10n.bookingsViewBooking,
                style: const TextStyle(
                  fontSize: 12,
                  fontWeight: FontWeight.w700,
                  color: KhadraColors.accent,
                ),
              ),
            ],
          ),
        ],
      ),
    );
  }
}
