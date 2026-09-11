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
import '../catalogue/vehicle_card.dart';
import 'shortlist_providers.dart';

/// The cars a customer has saved.
///
/// Two kinds of row, and the second is the interesting one. A car that is still
/// listed renders as an ordinary catalogue card, with today's rate — never the
/// price it had when it was saved, which would be a figure this screen made up.
/// A car that is no longer listed renders as its own thing: the date it was
/// saved, a sentence saying it has gone, and a way to remove it.
///
/// **That second row names nothing and explains nothing**, because the server
/// sends nothing: a hidden car, a deleted one, one in maintenance and a suspended
/// gallery's are indistinguishable through the catalogue on purpose, and a reason
/// here would undo that.
///
/// Nothing on this screen says whether a car is AVAILABLE. A saved car carries no
/// dates, and "is it free" has no answer without a period to ask about.
class ShortlistScreen extends ConsumerWidget {
  const ShortlistScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);
    final saved = ref.watch(shortlistProvider);
    final formats = ref.watch(formatsProvider);

    return Scaffold(
      appBar: AppBar(
        leading: const KhadraBack(fallback: Routes.profile),
        title: Text(l10n.shortlistTitle),
      ),
      body: RefreshIndicator(
        onRefresh: () async {
          ref.invalidate(shortlistProvider);
          await ref.read(shortlistProvider.future);
        },
        child: switch (saved) {
          AsyncLoading() => const KhadraLoading(),
          AsyncError(:final error) => KhadraError(
              message: ApiFailure.from(error).messageFor(l10n),
              onRetry: () => ref.invalidate(shortlistProvider),
            ),
          AsyncData(:final value) when value.isEmpty => ListView(
              children: [
                SizedBox(
                  height: MediaQuery.of(context).size.height * 0.55,
                  child: KhadraEmpty(
                    icon: Icons.favorite_border,
                    title: l10n.shortlistEmptyTitle,
                    body: l10n.shortlistEmptyBody,
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
              itemCount: value.length,
              separatorBuilder: (_, __) => const SizedBox(height: Space.lg),
              itemBuilder: (_, index) => switch (value[index]) {
                SavedVehicle(listing: final listing?) =>
                  VehicleCard(listing: listing),
                final gone => _NoLongerListed(saved: gone, formats: formats),
              },
            ),
          _ => const KhadraLoading(),
        },
      ),
    );
  }
}

/// A saved car the customer can no longer see.
class _NoLongerListed extends ConsumerStatefulWidget {
  const _NoLongerListed({required this.saved, required this.formats});

  final SavedVehicle saved;
  final Formats formats;

  @override
  ConsumerState<_NoLongerListed> createState() => _NoLongerListedState();
}

class _NoLongerListedState extends ConsumerState<_NoLongerListed> {
  bool _removing = false;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);

    return KhadraCard(
      background: KhadraColors.neutral100,
      child: Row(
        children: [
          const Icon(Icons.no_transfer_outlined,
              size: 28, color: KhadraColors.neutral400),
          const SizedBox(width: Space.md),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  l10n.shortlistNoLongerListed,
                  style: const TextStyle(
                      fontSize: 15, fontWeight: FontWeight.w600),
                ),
                const SizedBox(height: 2),
                Text(
                  // The only thing this row can honestly say about the car: when
                  // its owner saved it.
                  l10n.shortlistSavedOn(
                      widget.formats.longDate(widget.saved.savedAt)),
                  style: const TextStyle(
                      color: KhadraColors.neutral600, fontSize: 12),
                ),
              ],
            ),
          ),
          _removing
              ? const Padding(
                  padding: EdgeInsets.all(Space.md),
                  child: SizedBox(
                    width: 18,
                    height: 18,
                    child: CircularProgressIndicator(strokeWidth: 2),
                  ),
                )
              : IconButton(
                  onPressed: _remove,
                  icon: const Icon(Icons.delete_outline),
                  tooltip: l10n.shortlistRemove,
                  color: KhadraColors.neutral600,
                ),
        ],
      ),
    );
  }

  /// Removing works even though the car is gone.
  ///
  /// The server deliberately does not check visibility on the way out: this entry
  /// is precisely the one somebody most wants rid of, and a check would trap it
  /// on their list for ever.
  Future<void> _remove() async {
    final l10n = AppLocalizations.of(context);
    setState(() => _removing = true);

    final failure = await ref
        .read(savedVehiclesProvider.notifier)
        .forget(widget.saved.vehicleId);

    if (!mounted) return;
    setState(() => _removing = false);

    if (failure != null) {
      showKhadraMessage(
        context,
        ApiFailure.from(failure).messageFor(l10n),
        isError: true,
      );
    }
  }
}
