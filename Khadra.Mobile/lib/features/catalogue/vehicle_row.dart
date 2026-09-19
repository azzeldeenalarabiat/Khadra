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
import '../shortlist/save_button.dart';

/// One car in a list: a photograph beside its name, its office and its price.
///
/// **This is the shape the design uses wherever cars are listed** — search
/// results, a gallery's own page, and saved cars — and it replaced a tall card
/// with a 16:10 photograph on top. The tall card is the design's HOME treatment,
/// for a short "recommended" strip where three cars are the whole screen; a list
/// somebody is scanning gets four or five rows in the same height instead of one
/// and a half, which is what a list is for.
///
/// The trailing slot sits on the NAME line, which is where the design puts the
/// only thing it trails a row with. It is not a third column: a column takes its
/// width from the whole row, and the specification chips underneath are the part
/// that runs out of room first -- in Arabic, and in English on a 375 screen.
class VehicleRow extends ConsumerWidget {
  const VehicleRow({
    super.key,
    required this.listing,
    this.trailing,
    this.dense = false,
  });

  final CatalogueListing listing;

  /// What sits at the end of the row. Null leaves the price the last thing read.
  final Widget? trailing;

  /// The design's two thumbnail sizes: 104×88 where the row carries its
  /// specifications, 92×74 where it does not.
  final bool dense;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);
    final formats = ref.watch(formatsProvider);
    final arabic = ref.watch(isArabicProvider);
    final city = ref.watch(cityNameProvider(listing.gallery.cityId));

    return KhadraCard(
      padding: const EdgeInsets.all(Space.md),
      onTap: () => context.push(Routes.vehicle(listing.vehicleId)),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          SizedBox(
            width: dense ? 92 : 104,
            height: dense ? 74 : 88,
            child: KhadraImage(
              url: listing.coverImageUrl,
              borderRadius: Radii.field,
            ),
          ),
          const SizedBox(width: Space.md),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Expanded(
                      child: Text(
                        listing.title,
                        style: TextStyle(
                          fontSize: 14,
                          fontWeight: FontWeight.w800,
                          letterSpacing: KhadraType.of(context, -0.2),
                        ),
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                      ),
                    ),
                    const SizedBox(width: Space.sm),
                    _Rating(gallery: listing.gallery),
                    if (trailing != null) trailing!,
                  ],
                ),
                const SizedBox(height: 3),
                Text(
                  // The office and where it is — the two things that decide
                  // between two identical Corollas.
                  [listing.gallery.businessName, if (city != null) city]
                      .join(' · '),
                  style: const TextStyle(
                    fontSize: 11,
                    fontWeight: FontWeight.w600,
                    color: KhadraColors.neutral600,
                  ),
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                ),
                if (!dense) ...[
                  const SizedBox(height: 7),
                  // The transmission's words are the SERVER's, in both languages,
                  // from /app-config's vocabularies — the app holds no table of
                  // its own, and a value the platform adds later reads correctly
                  // without a release.
                  _Specs(labels: <String>[
                    _vocabulary(ref, listing.transmission, arabic),
                    l10n.vehicleSeats(listing.seats),
                    if (listing.carType != null) listing.carType!.nameFor(arabic),
                  ]),
                ],
                const SizedBox(height: 9),
                if (formats != null)
                  _Price(listing: listing, formats: formats, l10n: l10n),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

/// The daily rate, and the unit it is per.
///
/// One `Text.rich` rather than two widgets, so the two halves stay on one line
/// and wrap together — and so the amount keeps its bidi isolate inside Arabic.
class _Price extends StatelessWidget {
  const _Price({required this.listing, required this.formats, required this.l10n});

  final CatalogueListing listing;
  final Formats formats;
  final AppLocalizations l10n;

  @override
  Widget build(BuildContext context) => Text.rich(
        TextSpan(
          children: [
            TextSpan(
              text: formats.money(listing.dailyRate),
              style: const TextStyle(
                fontSize: 15,
                fontWeight: FontWeight.w800,
                color: KhadraColors.price,
              ),
            ),
            TextSpan(
              text: ' ${l10n.vehiclePerDay('')}',
              style: const TextStyle(
                fontSize: 11,
                fontWeight: FontWeight.w600,
                color: KhadraColors.neutral600,
              ),
            ),
          ],
        ),
        maxLines: 1,
        overflow: TextOverflow.ellipsis,
      );
}

/// The platform's own word for a value, in the reader's language.
///
/// Falls back to the raw name rather than to nothing: a transmission this build
/// has never heard of should read as itself on a card, not vanish from it.
String _vocabulary(WidgetRef ref, String name, bool arabic) {
  final entries =
      ref.watch(appConfigProvider).valueOrNull?.vocabularies.transmissions ??
          const <VocabularyEntry>[];
  for (final entry in entries) {
    if (entry.name == name) return entry.labelFor(arabic);
  }
  return name;
}

/// The three facts a reader compares cars on, as the design's grey chips.
class _Specs extends StatelessWidget {
  const _Specs({required this.labels});

  final List<String> labels;

  @override
  Widget build(BuildContext context) {
    return Wrap(
      spacing: 5,
      runSpacing: 5,
      children: [
        for (final label in labels)
          Container(
            padding: const EdgeInsets.symmetric(horizontal: 7, vertical: 4),
            decoration: const BoxDecoration(
              color: KhadraColors.neutral100,
              borderRadius: Radii.pill,
            ),
            child: Text(
              label,
              style: const TextStyle(
                fontSize: 10,
                fontWeight: FontWeight.w600,
                color: KhadraColors.neutral700,
              ),
            ),
          ),
      ],
    );
  }
}

/// A gallery's rating, or nothing at all.
///
/// Null is not zero. Zero is a real score on a one-to-five scale, and rendering
/// an unrated office as zero would show it as the worst on the platform — so an
/// unrated one shows no star, which in a row this dense is also the quietest
/// thing it could do.
class _Rating extends StatelessWidget {
  const _Rating({required this.gallery});

  final CatalogueGalleryLabel gallery;

  @override
  Widget build(BuildContext context) {
    if (gallery.averageRating == null || gallery.reviewCount == 0) {
      return const SizedBox.shrink();
    }

    return Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        const Icon(Icons.star_rounded, size: 12, color: KhadraColors.star),
        const SizedBox(width: 3),
        LatinRun(
          gallery.averageRating!.toStringAsFixed(1),
          style: const TextStyle(
            fontSize: 12,
            fontWeight: FontWeight.w700,
            color: KhadraColors.text,
          ),
        ),
      ],
    );
  }
}

/// The heart, sized and spaced for the end of a row rather than for a photograph.
class VehicleRowSaveButton extends StatelessWidget {
  const VehicleRowSaveButton({super.key, required this.vehicleId});

  final String vehicleId;

  /// A 44dp target that OCCUPIES 22 of layout height.
  ///
  /// It sits on the name line, and a 44-high button there would set the height
  /// of that line: the office underneath would drop half a row away from the car
  /// it belongs to, and the card would grow by the difference. The overflow
  /// spills into the card's own padding, where there is nothing to collide with
  /// and nothing clipping it, so the finger still gets its full 44.
  static const double _line = 22;
  static const double _target = 44;

  @override
  Widget build(BuildContext context) => SizedBox(
        width: _target,
        height: _line,
        child: OverflowBox(
          minHeight: _target,
          maxHeight: _target,
          child: SaveButton(vehicleId: vehicleId, size: 20),
        ),
      );
}

/// The price on the vehicle screen's sticky booking bar.
///
/// The design's own shape for it: the rate and its unit on ONE line, the rate in
/// the dark green it reserves for money and the unit in the muted grey beside it,
/// so the bar reads as a price with a button rather than as two stacked labels.
class DailyRateLabel extends StatelessWidget {
  const DailyRateLabel({
    super.key,
    required this.formats,
    required this.rate,
    this.size = 17,
    this.stacked = false,
  });

  final Formats formats;
  final Money rate;

  /// The price's own size; the unit sits two steps under it, as the design draws
  /// it at both the sizes it uses (17 in the booking bar, 20 on the car itself).
  final double size;

  /// Put the unit on its own line, for the narrow right-hand column of a title
  /// block where one long line would wrap in the middle of the amount.
  final bool stacked;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);

    // One source for the unit, and it is the SAME translated string in both
    // shapes: the amount is empty here because the price is typeset separately.
    final unit = l10n.vehiclePerDay('').trim();
    final amountStyle = TextStyle(
      fontSize: size,
      fontWeight: FontWeight.w800,
      color: KhadraColors.price,
    );
    final unitStyle = TextStyle(
      // The unit is 12 at both sizes the design uses it -- it is a unit, not a
      // smaller price, so it does not grow with the number beside it.
      fontSize: 12,
      fontWeight: FontWeight.w600,
      color: KhadraColors.neutral600,
    );

    if (stacked) {
      return Column(
        crossAxisAlignment: CrossAxisAlignment.end,
        children: [
          Text(formats.money(rate), style: amountStyle, maxLines: 1),
          Text(unit, style: unitStyle, maxLines: 1),
        ],
      );
    }

    return Text.rich(
      TextSpan(
        children: [
          TextSpan(text: formats.money(rate), style: amountStyle),
          TextSpan(text: ' $unit', style: unitStyle),
        ],
      ),
      maxLines: 1,
      overflow: TextOverflow.ellipsis,
    );
  }
}
