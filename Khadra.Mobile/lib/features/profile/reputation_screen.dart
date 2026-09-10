import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../api/dtos.dart';
import '../../core/api/api_failure.dart';
import '../../core/api/api_failure_messages.dart';
import '../../core/format/formats.dart';
import '../../core/providers.dart';
import '../../core/router.dart';
import '../../core/theme/khadra_theme.dart';
import '../../core/widgets/khadra_widgets.dart';
import '../../l10n/app_localizations.dart';
import '../bookings/booking_providers.dart';

/// What rental offices are told about you.
///
/// This exists for one reason and it is not vanity: a semi-private score somebody
/// cannot see is what privacy law objects to, and it is the only way a customer
/// learns of a no-show recorded against them while the window to dispute it is
/// still open.
///
/// **Nothing here is computed by this screen.** No grade, no colour band, no
/// "trust level" from the counts — the server publishes six figures and a verdict
/// on whether there is a history at all, and the screen renders those. A number
/// this app invented about a customer's standing would be a second opinion the
/// galleries never see.
///
/// The rating is rendered as the server's one-decimal figure. There is
/// deliberately no star widget over it: stars round, and a 3.4 shown as three
/// and a half stars is a different claim from the one the galleries read.
class ReputationScreen extends ConsumerWidget {
  const ReputationScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);
    final reputation = ref.watch(myReputationProvider);
    final formats = ref.watch(formatsProvider);

    return Scaffold(
      appBar: AppBar(
        leading: const KhadraBack(fallback: Routes.profile),
        title: Text(l10n.reputationTitle),
      ),
      body: RefreshIndicator(
        onRefresh: () async {
          ref.invalidate(myReputationProvider);
          await ref.read(myReputationProvider.future);
        },
        child: switch (reputation) {
          AsyncLoading() => const KhadraLoading(),
          AsyncError(:final error) => KhadraError(
              message: ApiFailure.from(error).messageFor(l10n),
              onRetry: () => ref.invalidate(myReputationProvider),
            ),
          AsyncData(:final value) when formats != null =>
            _Body(reputation: value, formats: formats),
          _ => const KhadraLoading(),
        },
      ),
    );
  }
}

class _Body extends ConsumerWidget {
  const _Body({required this.reputation, required this.formats});

  final CustomerReputation reputation;
  final Formats formats;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);

    return ListView(
      padding: const EdgeInsets.fromLTRB(
          Space.lg, Space.lg, Space.lg, Space.bottomInset),
      children: [
        Text(
          l10n.reputationIntro,
          style: const TextStyle(fontSize: 14, height: 1.55),
        ),
        const SizedBox(height: Space.lg),

        // "Nothing recorded" and "a clean record" are different things to be
        // told, and `hasHistory` is the server's answer to which one this is —
        // never a screen adding up zeros.
        if (!reputation.hasHistory) ...[
          KhadraNotice(
            title: l10n.reputationNoHistoryTitle,
            body: l10n.reputationNoHistoryBody,
            tone: NoticeTone.neutral,
            icon: Icons.history_toggle_off_outlined,
          ),
          const SizedBox(height: Space.lg),
          _SinceCard(reputation: reputation, formats: formats),
        ] else ...[
          _RatingCard(reputation: reputation),
          const SizedBox(height: Space.lg),
          KhadraSectionTitle(l10n.reputationRecordTitle),
          KhadraCard(
            child: Column(
              children: [
                KhadraDetailRow(
                  label: l10n.reputationCompletedRentals,
                  value: LatinRun(reputation.completedRentals.toString()),
                ),
                KhadraDetailRow(
                  label: l10n.reputationNoShows,
                  value: LatinRun(reputation.noShows.toString()),
                ),
                // "Assessed", never "charged". Spec 3.3: with no dispute ticket
                // nothing is applied at all, and this screen must not imply money
                // moved.
                KhadraDetailRow(
                  label: l10n.reputationLateCancellations,
                  value: LatinRun(reputation.lateCancellations.toString()),
                ),
                KhadraDetailRow(
                  label: l10n.reputationDisputesLost,
                  value: LatinRun(
                      reputation.disputesResolvedAgainstCustomer.toString()),
                ),
              ],
            ),
          ),

          if (reputation.hasMarks) ...[
            const SizedBox(height: Space.lg),
            KhadraNotice(
              title: l10n.reputationDisagreeTitle,
              body: l10n.reputationDisagreeBody,
              tone: NoticeTone.neutral,
              icon: Icons.gavel_outlined,
              // Navigation, not a second source of data: the bookings tab is
              // already the only place these can be looked at one by one, and it
              // reads them itself.
              action: OutlinedButton(
                onPressed: () {
                  ref.read(selectedBookingTabProvider.notifier).state =
                      BookingTabs.closed;
                  context.go(Routes.bookings);
                },
                child: Text(l10n.reputationSeeBookings),
              ),
            ),
          ],

          const SizedBox(height: Space.lg),
          _SinceCard(reputation: reputation, formats: formats),
        ],

        const SizedBox(height: Space.lg),
        Text(
          l10n.reputationWhoSeesThis,
          style: const TextStyle(
              color: KhadraColors.neutral600, fontSize: 13, height: 1.5),
        ),
      ],
    );
  }
}

/// The rating galleries leave, or an honest silence.
class _RatingCard extends StatelessWidget {
  const _RatingCard({required this.reputation});

  final CustomerReputation reputation;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final average = reputation.averageRating;

    // Null is not zero. Zero is a real score on a one-to-five scale, and an
    // unrated customer shown as zero reads as the worst on the platform.
    if (average == null || reputation.ratingCount == 0) {
      return KhadraCard(
        child: Row(
          children: [
            const Icon(Icons.star_outline_rounded,
                size: 28, color: KhadraColors.neutral300),
            const SizedBox(width: Space.md),
            Expanded(
              child: Text(
                l10n.reputationNotRatedYet,
                style: const TextStyle(
                    color: KhadraColors.neutral600, fontSize: 14, height: 1.45),
              ),
            ),
          ],
        ),
      );
    }

    return KhadraCard(
      child: Row(
        children: [
          // The server's own one-decimal figure. Stars would round it, and a 3.4
          // drawn as three and a half is a different claim from the one the
          // galleries are reading.
          LatinRun(
            average.toStringAsFixed(1),
            style: const TextStyle(
              fontSize: 30,
              fontWeight: FontWeight.w700,
              color: KhadraColors.text,
            ),
          ),
          const SizedBox(width: Space.md),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  l10n.reputationRatingLabel,
                  style: const TextStyle(
                      fontSize: 15, fontWeight: FontWeight.w600),
                ),
                const SizedBox(height: 2),
                Text(
                  l10n.reputationRatingCount(reputation.ratingCount),
                  style: const TextStyle(
                      color: KhadraColors.neutral600, fontSize: 13),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

class _SinceCard extends StatelessWidget {
  const _SinceCard({required this.reputation, required this.formats});

  final CustomerReputation reputation;
  final Formats formats;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);

    return KhadraCard(
      child: Row(
        children: [
          const Icon(Icons.event_available_outlined,
              size: 20, color: KhadraColors.neutral600),
          const SizedBox(width: Space.md),
          Expanded(
            child: Text(
              l10n.profileMemberSince(formats.longDate(reputation.customerSince)),
              style: const TextStyle(fontSize: 14),
            ),
          ),
        ],
      ),
    );
  }
}
