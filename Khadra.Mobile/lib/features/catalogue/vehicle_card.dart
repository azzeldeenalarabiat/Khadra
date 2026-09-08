import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../api/dtos.dart';
import '../../core/format/formats.dart';
import '../../core/providers.dart';
import '../../core/router.dart';
import '../../core/theme/khadra_theme.dart';
import '../../core/widgets/khadra_widgets.dart';
import '../../l10n/app_localizations.dart';

/// One car, as a search result.
///
/// The rating on it is the GALLERY's, not the car's, and the note under the list
/// says so. This platform rates rental offices — a renter comparing two Corollas
/// is really choosing between two offices, which is what the spec models and what
/// this shows.
class VehicleCard extends ConsumerWidget {
  const VehicleCard({super.key, required this.listing});

  final CatalogueListing listing;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);
    final formats = ref.watch(formatsProvider);
    final arabic = ref.watch(isArabicProvider);

    return KhadraCard(
      padding: EdgeInsets.zero,
      onTap: () => context.push(Routes.vehicle(listing.vehicleId)),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Stack(
            children: [
              AspectRatio(
                aspectRatio: 16 / 10,
                child: KhadraImage(
                  url: listing.coverImageUrl,
                  borderRadius: const BorderRadius.vertical(top: Radii.lg),
                ),
              ),
              if (listing.isDeliveryAvailable)
                PositionedDirectional(
                  top: Space.sm,
                  start: Space.sm,
                  child: KhadraBadge(
                    label: l10n.vehicleDeliveryAvailable,
                    colour: KhadraColors.accent,
                    icon: Icons.local_shipping_outlined,
                  ),
                ),
            ],
          ),
          Padding(
            padding: const EdgeInsets.all(Space.lg),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(
                            listing.title,
                            style: const TextStyle(
                              fontSize: 17,
                              fontWeight: FontWeight.w700,
                            ),
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                          ),
                          const SizedBox(height: 2),
                          Text(
                            _subtitle(l10n, arabic),
                            style: const TextStyle(
                              color: KhadraColors.neutral600,
                              fontSize: 13,
                            ),
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                          ),
                        ],
                      ),
                    ),
                    const SizedBox(width: Space.md),
                    if (formats != null)
                      Column(
                        crossAxisAlignment: CrossAxisAlignment.end,
                        children: [
                          Text(
                            formats.money(listing.dailyRate),
                            style: const TextStyle(
                              fontSize: 17,
                              fontWeight: FontWeight.w700,
                              color: KhadraColors.accent,
                            ),
                          ),
                          Text(
                            l10n.vehiclePerDay(''),
                            style: const TextStyle(
                              color: KhadraColors.neutral500,
                              fontSize: 12,
                            ),
                          ),
                        ],
                      ),
                  ],
                ),
                const SizedBox(height: Space.md),
                const Divider(height: 1),
                const SizedBox(height: Space.md),
                Row(
                  children: [
                    Expanded(
                      child: Text(
                        listing.gallery.businessName,
                        style: const TextStyle(
                          fontSize: 13,
                          fontWeight: FontWeight.w600,
                          color: KhadraColors.neutral700,
                        ),
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                      ),
                    ),
                    const SizedBox(width: Space.sm),
                    _Rating(gallery: listing.gallery),
                  ],
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }

  /// Year, car type, transmission and seats, joined only where each exists.
  ///
  /// The car type can legitimately be null — an administrator may have retired the
  /// category since — and its name comes in both languages from the API rather
  /// than from a table in the app.
  String _subtitle(AppLocalizations l10n, bool arabic) {
    final parts = <String>[
      listing.year.toString(),
      if (listing.carType != null) listing.carType!.nameFor(arabic),
      l10n.vehicleSeats(listing.seats),
    ];
    return parts.join(' · ');
  }
}

/// A gallery's rating, or an honest silence.
///
/// Null is not zero. Zero is a real score on a one-to-five scale, and rendering an
/// unrated office as zero stars would show it as the worst on the platform.
class _Rating extends StatelessWidget {
  const _Rating({required this.gallery});

  final CatalogueGalleryLabel gallery;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);

    if (gallery.averageRating == null || gallery.reviewCount == 0) {
      return Text(
        l10n.galleryNotRatedYet,
        style: const TextStyle(color: KhadraColors.neutral500, fontSize: 12),
      );
    }

    return Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        const Icon(Icons.star_rounded, size: 15, color: KhadraColors.warn),
        const SizedBox(width: 3),
        LatinRun(
          gallery.averageRating!.toStringAsFixed(1),
          style: const TextStyle(
            fontSize: 13,
            fontWeight: FontWeight.w700,
            color: KhadraColors.text,
          ),
        ),
        const SizedBox(width: 4),
        Text(
          '(${gallery.reviewCount})',
          style: const TextStyle(color: KhadraColors.neutral500, fontSize: 12),
        ),
      ],
    );
  }
}

/// The price line used on the vehicle screen's sticky bar.
class DailyRateLabel extends StatelessWidget {
  const DailyRateLabel({
    super.key,
    required this.formats,
    required this.rate,
  });

  final Formats formats;
  final Money rate;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      mainAxisSize: MainAxisSize.min,
      children: [
        Text(
          formats.money(rate),
          style: const TextStyle(
            fontSize: 20,
            fontWeight: FontWeight.w700,
            color: KhadraColors.text,
          ),
        ),
        Text(
          l10n.vehiclePerDay(''),
          style: const TextStyle(color: KhadraColors.neutral500, fontSize: 12),
        ),
      ],
    );
  }
}
