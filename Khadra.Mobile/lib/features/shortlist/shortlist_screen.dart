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
/// Two kinds of row, and the second is the interesting one. A car that can still
/// be booked renders as an ordinary catalogue card, with today's rate — never the
/// price it had when it was saved, which would be a figure this screen made up.
/// A car that cannot renders as a quieter version of itself: still named, still
/// carrying its gallery, marked **Currently unavailable**, with no price, no way
/// through to it and no way to start a booking from it.
///
/// **It names the car. It never names a reason**, because the server sends none:
/// a hidden car, a deleted one, one in maintenance and a suspended gallery's are
/// indistinguishable through the catalogue on purpose, and a reason here would
/// undo that. Naming the car is not the same disclosure — nothing reaches this
/// list that the customer was not shown in the catalogue first.
///
/// **Nothing is ever removed for them.** `Maintenance → Hidden → Active` is a
/// routine round trip for a gallery, and the owner settled on 2026-09-11 that an
/// entry survives all of it. Only the customer's own tap removes a row.
///
/// Nothing on this screen says whether a car is AVAILABLE for dates. A saved car
/// carries no dates, and "is it free" has no answer without a period to ask about.
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
          AsyncData(:final value) when formats != null => _SavedList(
              saved: value,
              formats: formats,
            ),
          _ => const KhadraLoading(),
        },
      ),
    );
  }
}

/// The rows, and the one thing they tell the heart.
///
/// Every car here is saved by definition, so the membership set is TOLD rather
/// than asked. Without it the saved-cars screen drew an empty heart on every car
/// it was showing — the set is filled per page of catalogue results, and a car
/// that is no longer listed never appears on one.
class _SavedList extends ConsumerStatefulWidget {
  const _SavedList({required this.saved, required this.formats});

  final List<SavedVehicle> saved;
  final Formats formats;

  @override
  ConsumerState<_SavedList> createState() => _SavedListState();
}

class _SavedListState extends ConsumerState<_SavedList> {
  @override
  void initState() {
    super.initState();
    _tell();
  }

  @override
  void didUpdateWidget(_SavedList old) {
    super.didUpdateWidget(old);
    _tell();
  }

  void _tell() {
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted) return;
      ref.read(savedVehiclesProvider.notifier).markSaved(
            [for (final entry in widget.saved) entry.vehicleId],
          );
    });
  }

  @override
  Widget build(BuildContext context) {
    final value = widget.saved;
    final formats = widget.formats;
    return ListView.separated(
      padding: const EdgeInsets.fromLTRB(
          Space.lg, Space.lg, Space.lg, Space.bottomInset),
      itemCount: value.length,
      separatorBuilder: (_, __) => const SizedBox(height: Space.lg),
      itemBuilder: (_, index) => switch (value[index]) {
        SavedVehicle(listing: final listing?) => VehicleCard(listing: listing),
        final gone => _Unavailable(saved: gone, formats: formats),
      },
    );
  }
}

/// A saved car the customer cannot book right now.
///
/// Held at three-quarter opacity, which is the design's way of saying "yours,
/// still here, not actionable" — the card keeps its shape rather than becoming a
/// different kind of object, so the list still reads as one list. There is
/// deliberately no tap target anywhere on it and no price: a booking cannot start
/// from here, and a rate for a car nobody can rent is a number with no meaning.
class _Unavailable extends ConsumerStatefulWidget {
  const _Unavailable({required this.saved, required this.formats});

  final SavedVehicle saved;
  final Formats formats;

  @override
  ConsumerState<_Unavailable> createState() => _UnavailableState();
}

class _UnavailableState extends ConsumerState<_Unavailable> {
  bool _removing = false;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final identity = widget.saved.identity;

    return Opacity(
      opacity: 0.75,
      child: KhadraCard(
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            // Where the photograph would be. A real one is out of the question —
            // vehicle images are served from static storage by key, so a URL for a
            // car that has been withdrawn would be that car's photograph still on
            // the open internet.
            Container(
              width: 76,
              height: 60,
              decoration: BoxDecoration(
                color: KhadraColors.neutral100,
                borderRadius: Radii.field,
              ),
              child: const Icon(Icons.directions_car_outlined,
                  size: 22, color: KhadraColors.neutral400),
            ),
            const SizedBox(width: Space.md),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    // Named where the server could name it; where it could not,
                    // the date it was saved is the only honest thing left to say.
                    identity?.title ?? l10n.shortlistUnavailable,
                    style: const TextStyle(
                        fontSize: 15, fontWeight: FontWeight.w700),
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                  ),
                  const SizedBox(height: 2),
                  Text(
                    identity?.galleryName ??
                        l10n.shortlistSavedOn(
                            widget.formats.longDate(widget.saved.savedAt)),
                    style: const TextStyle(
                        color: KhadraColors.neutral600, fontSize: 12),
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                  ),
                  const SizedBox(height: Space.sm),
                  // The pill. No reason on it, and none available to put there.
                  Container(
                    padding: const EdgeInsets.symmetric(
                        horizontal: Space.sm, vertical: 4),
                    decoration: BoxDecoration(
                      color: KhadraColors.neutral100,
                      borderRadius: const BorderRadius.all(Radii.sm),
                    ),
                    child: Text(
                      l10n.shortlistUnavailable.toUpperCase(),
                      style: const TextStyle(
                        fontSize: 10,
                        fontWeight: FontWeight.w700,
                        letterSpacing: 0.4,
                        color: KhadraColors.neutral600,
                      ),
                    ),
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
