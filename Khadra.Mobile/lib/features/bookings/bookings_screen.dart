import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../api/dtos.dart';
import '../../core/api/api_failure.dart';
import '../../core/api/api_failure_messages.dart';
import '../../core/format/booking_presentation.dart';
import '../../core/format/formats.dart';
import '../../core/providers.dart';
import '../../core/router.dart';
import '../../core/theme/khadra_theme.dart';
import '../../core/widgets/khadra_widgets.dart';
import '../../l10n/app_localizations.dart';
import 'booking_providers.dart';

class BookingsScreen extends ConsumerWidget {
  const BookingsScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);
    final session = ref.watch(sessionProvider);

    if (!session.isSignedIn) {
      return Scaffold(
        appBar: AppBar(title: Text(l10n.bookingsTitle)),
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
        title: Text(l10n.bookingsTitle),
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
          AsyncData(:final value) when value.items.isEmpty => ListView(
              // Inside a scroll view so pull-to-refresh still works on an empty
              // list, which is exactly when somebody is most likely to try it.
              children: [
                SizedBox(
                  height: MediaQuery.of(context).size.height * 0.55,
                  child: KhadraEmpty(
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
            ),
          AsyncData(:final value) when formats != null => ListView.separated(
              padding: const EdgeInsets.fromLTRB(
                  Space.lg, Space.lg, Space.lg, Space.bottomInset),
              itemCount: value.items.length,
              separatorBuilder: (_, __) => const SizedBox(height: Space.md),
              itemBuilder: (_, index) =>
                  _BookingRow(booking: value.items[index], formats: formats),
            ),
          _ => const KhadraLoading(),
        },
      ),
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
                width: 76,
                height: 58,
                child: KhadraImage(
                  url: booking.vehicle?.coverImageUrl,
                  borderRadius: const BorderRadius.all(Radii.md),
                ),
              ),
              const SizedBox(width: Space.md),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      // The car can be null: a booking is a financial record that
                      // outlives the listing behind it.
                      booking.vehicle?.title ?? booking.dealerName,
                      style: const TextStyle(
                          fontSize: 15, fontWeight: FontWeight.w700),
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                    ),
                    const SizedBox(height: 2),
                    Text(
                      booking.dealerName,
                      style: const TextStyle(
                          color: KhadraColors.neutral600, fontSize: 12),
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                    ),
                    const SizedBox(height: Space.xs),
                    Text(
                      // The DAYS figure is frozen on the booking. Recomputing it
                      // from the two instants would answer a different question
                      // from the one the invoice was written against.
                      '${formats.dateRange(booking.periodStart, booking.periodEnd)} · ${l10n.bookDays(booking.days)}',
                      style: const TextStyle(
                          color: KhadraColors.neutral600, fontSize: 12),
                    ),
                  ],
                ),
              ),
            ],
          ),
          const SizedBox(height: Space.md),
          Row(
            children: [
              KhadraBadge(
                label: BookingPresentation.label(l10n, booking.status),
                colour: BookingPresentation.colour(booking.status),
                icon: BookingPresentation.icon(booking.status),
              ),
              if (booking.hasLiveDispute) ...[
                const SizedBox(width: Space.sm),
                KhadraBadge(
                  label: l10n.bookingsTabDisputed,
                  colour: KhadraColors.bad,
                  icon: Icons.gavel_outlined,
                ),
              ],
              const Spacer(),
              Text(
                formats.moneyOf(booking.totalPrice, booking.currency),
                style: const TextStyle(
                    fontSize: 14, fontWeight: FontWeight.w700),
              ),
            ],
          ),
          const SizedBox(height: Space.sm),
          LatinRun(
            booking.reference,
            style: const TextStyle(
              fontSize: 11,
              color: KhadraColors.neutral500,
              letterSpacing: 0.4,
            ),
          ),
        ],
      ),
    );
  }
}
