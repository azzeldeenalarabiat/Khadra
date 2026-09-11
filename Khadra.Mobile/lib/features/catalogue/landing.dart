import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../api/dtos.dart';
import '../../core/providers.dart';
import '../../core/router.dart';
import '../../core/theme/khadra_theme.dart';
import '../../core/widgets/khadra_widgets.dart';
import '../../l10n/app_localizations.dart';
import '../bookings/booking_providers.dart';
import 'search_providers.dart';

/// What the first screen shows above the results.
///
/// The owner asked for a home page. This is the honest version of one: a
/// marketplace's home IS its search, and everything a conventional home page
/// carries — featured cars, "popular near you", promotions — has no endpoint
/// behind it on this platform. A "featured" list would be somebody picking cars
/// by hand, which is the static-data rule broken on the app's front door.
///
/// So what is here is only what is REAL: the two lookups the filter sheet already
/// loads, and the one booking the server says needs attention.
class SearchLanding extends ConsumerWidget {
  const SearchLanding({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final filter = ref.watch(searchFilterProvider);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        const _NextBookingCard(),
        // Quick entry disappears once the customer has narrowed anything: at that
        // point they are reading results, and a row of shortcuts above them is
        // just something between the search and its answer.
        if (filter.activeCount == 0 && (filter.text ?? '').isEmpty)
          const _QuickEntry(),
      ],
    );
  }
}

/// The booking that needs the customer's attention, if there is one.
///
/// **Which booking this is comes from the server**, with the reason it was
/// chosen. There is no push channel on this platform yet, so a customer learns
/// their request was approved by opening the app — and the deposit window is 24
/// hours precisely because of that. This card is the only thing that reaches
/// somebody in time.
///
/// It leads to the booking, never to a payment: paying is still refused while no
/// provider is configured, and a "Pay now" here would walk a customer into a 503.
class _NextBookingCard extends ConsumerWidget {
  const _NextBookingCard();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final next = ref.watch(nextBookingProvider).valueOrNull;
    final formats = ref.watch(formatsProvider);
    if (next == null || formats == null) return const SizedBox.shrink();

    final l10n = AppLocalizations.of(context);
    final booking = next.booking;
    final urgent = next.reason == NextBookingReasons.awaitingPayment;

    return Padding(
      padding: const EdgeInsets.only(bottom: Space.lg),
      child: KhadraCard(
        onTap: () => context.push(Routes.booking(booking.bookingId)),
        borderColor: urgent ? KhadraColors.warn : KhadraColors.neutral200,
        background: urgent
            ? KhadraColors.warn.withValues(alpha: 0.05)
            : KhadraColors.surface,
        padding: const EdgeInsets.all(Space.md),
        child: Row(
          children: [
            SizedBox(
              width: 68,
              height: 52,
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
                  Text(
                    _headline(l10n, next.reason),
                    style: TextStyle(
                      fontSize: 14,
                      fontWeight: FontWeight.w700,
                      color: urgent ? KhadraColors.warn : KhadraColors.text,
                    ),
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                  ),
                  const SizedBox(height: 2),
                  Text(
                    // A booking outlives the listing behind it, so the car can be
                    // gone; the gallery's name is what identifies it then.
                    booking.vehicle?.title ?? booking.dealerName,
                    style: const TextStyle(
                        color: KhadraColors.neutral700, fontSize: 13),
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                  ),
                  const SizedBox(height: 2),
                  Text(
                    formats.dateRange(booking.periodStart, booking.periodEnd),
                    style: const TextStyle(
                        color: KhadraColors.neutral600, fontSize: 12),
                  ),
                ],
              ),
            ),
            const Icon(Icons.chevron_right, color: KhadraColors.neutral400),
          ],
        ),
      ),
    );
  }

  /// The sentence for the server's reason code.
  ///
  /// A code this build has never heard of falls back to the booking's own status
  /// label rather than to nothing: the platform can add a reason at any time, and
  /// an old app in a shop should degrade rather than render a blank line.
  String _headline(AppLocalizations l10n, String reason) => switch (reason) {
        NextBookingReasons.awaitingPayment => l10n.landingDepositDue,
        NextBookingReasons.inProgress => l10n.landingRentalInProgress,
        NextBookingReasons.upcoming => l10n.landingUpcomingRental,
        NextBookingReasons.awaitingDecision => l10n.landingAwaitingOffice,
        _ => l10n.bookingsTitle,
      };
}

/// Two rows of chips that fill in the search for you.
///
/// The cities and the car types are the platform's own lookups, in both
/// languages, already loaded and kept alive for the filter sheet — so this adds
/// no request. Tapping one writes into the SAME filter the sheet writes into,
/// rather than holding a second idea of what is being searched for.
///
/// Nothing here is ranked or "popular": the platform publishes no such figure,
/// and inventing an order would be a claim the app cannot support.
class _QuickEntry extends ConsumerWidget {
  const _QuickEntry();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);
    final arabic = ref.watch(isArabicProvider);
    final cities = ref.watch(citiesProvider).valueOrNull ?? const <Lookup>[];
    final carTypes = ref.watch(carTypesProvider).valueOrNull ?? const <Lookup>[];

    // A lookup that has not loaded shows nothing rather than an empty heading.
    if (cities.isEmpty && carTypes.isEmpty) return const SizedBox.shrink();

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        if (cities.isNotEmpty)
          _ChipRow(
            title: l10n.landingWhereTo,
            options: [
              for (final city in cities)
                (value: city.id, label: city.nameFor(arabic)),
            ],
            onTap: (value) => ref
                .read(searchFilterProvider.notifier)
                .update((filter) => filter.copyWith(cityId: value)),
          ),
        if (carTypes.isNotEmpty)
          _ChipRow(
            title: l10n.landingWhatKind,
            options: [
              for (final type in carTypes)
                (value: type.id, label: type.nameFor(arabic)),
            ],
            onTap: (value) => ref
                .read(searchFilterProvider.notifier)
                .update((filter) => filter.copyWith(carTypeId: value)),
          ),
        const SizedBox(height: Space.sm),
      ],
    );
  }
}

class _ChipRow extends StatelessWidget {
  const _ChipRow({
    required this.title,
    required this.options,
    required this.onTap,
  });

  final String title;
  final List<({String value, String label})> options;
  final ValueChanged<String> onTap;

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.only(bottom: Space.md),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              title,
              style: const TextStyle(
                fontSize: 13,
                fontWeight: FontWeight.w600,
                color: KhadraColors.neutral600,
              ),
            ),
            const SizedBox(height: Space.sm),
            SizedBox(
              height: 36,
              // Scrolls rather than wraps: the number of cities is the platform's
              // to grow, and a wrapping block would push the results off screen
              // the day an administrator adds a dozen.
              child: ListView.separated(
                scrollDirection: Axis.horizontal,
                itemCount: options.length,
                separatorBuilder: (_, __) => const SizedBox(width: Space.sm),
                itemBuilder: (_, index) => ActionChip(
                  label: Text(options[index].label),
                  onPressed: () => onTap(options[index].value),
                  visualDensity: VisualDensity.compact,
                ),
              ),
            ),
          ],
        ),
      );
}
