import 'package:flutter/material.dart';
import 'package:flutter_map/flutter_map.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:latlong2/latlong.dart';
import 'package:url_launcher/url_launcher.dart';

import '../../api/dtos.dart';
import '../../core/api/api_failure.dart';
import '../../core/api/api_failure_messages.dart';
import '../../core/format/formats.dart';
import '../../core/providers.dart';
import '../../core/router.dart';
import '../../core/theme/khadra_theme.dart';
import '../../core/widgets/khadra_widgets.dart';
import '../../l10n/app_localizations.dart';
import 'search_providers.dart';
import 'vehicle_card.dart';

/// A rental office's public page.
///
/// What is NOT here matters as much as what is: no commercial registration number,
/// no verification status, no suspension reason. A gallery that cannot trade is
/// not returned by the API at all, so a status field would only ever read
/// "approved" and invite somebody to add the others beside it.
class GalleryScreen extends ConsumerWidget {
  const GalleryScreen({super.key, required this.dealerId});

  final String dealerId;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);
    final gallery = ref.watch(galleryProvider(dealerId));
    final formats = ref.watch(formatsProvider);

    return Scaffold(
      appBar: AppBar(title: Text(l10n.galleryTitle)),
      body: switch (gallery) {
        AsyncLoading() => const KhadraLoading(),
        AsyncError(:final error) => KhadraError(
            message: ApiFailure.from(error).messageFor(l10n),
            onRetry: () => ref.invalidate(galleryProvider(dealerId)),
          ),
        AsyncData(:final value) when formats != null =>
          _GalleryBody(gallery: value, formats: formats),
        _ => const KhadraLoading(),
      },
    );
  }
}

class _GalleryBody extends ConsumerWidget {
  const _GalleryBody({required this.gallery, required this.formats});

  final PublicGallery gallery;
  final Formats formats;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);
    final vehicles = ref.watch(galleryVehiclesProvider(gallery.dealerId));

    return ListView(
      padding: const EdgeInsets.only(bottom: Space.bottomInset),
      children: [
        if (gallery.coverUrl != null)
          AspectRatio(
            aspectRatio: 16 / 7,
            child: KhadraImage(url: gallery.coverUrl),
          ),

        Padding(
          padding: const EdgeInsets.all(Space.lg),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  if (gallery.logoUrl != null)
                    Padding(
                      padding: const EdgeInsetsDirectional.only(end: Space.md),
                      child: SizedBox(
                        width: 56,
                        height: 56,
                        child: KhadraImage(
                          url: gallery.logoUrl,
                          fit: BoxFit.contain,
                          borderRadius: const BorderRadius.all(Radii.md),
                        ),
                      ),
                    ),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          gallery.businessName,
                          style: const TextStyle(
                              fontSize: 21, fontWeight: FontWeight.w700),
                        ),
                        const SizedBox(height: Space.xs),
                        _RatingLine(gallery: gallery),
                      ],
                    ),
                  ),
                ],
              ),

              if (gallery.description != null &&
                  gallery.description!.trim().isNotEmpty) ...[
                const SizedBox(height: Space.xl),
                KhadraSectionTitle(l10n.galleryAbout),
                Text(
                  gallery.description!,
                  style: const TextStyle(fontSize: 15, height: 1.55),
                ),
              ],

              const SizedBox(height: Space.xl),
              KhadraSectionTitle(l10n.galleryDelivery),
              KhadraNotice(
                title: gallery.delivery.isEnabled && gallery.delivery.fee != null
                    ? l10n.galleryDeliveryOffered(
                        gallery.delivery.radiusKm.toString(),
                        formats.money(gallery.delivery.fee!),
                      )
                    : l10n.galleryDeliveryNotOffered,
                tone: gallery.delivery.isEnabled
                    ? NoticeTone.accent
                    : NoticeTone.neutral,
                icon: Icons.local_shipping_outlined,
              ),

              const SizedBox(height: Space.xl),
              KhadraSectionTitle(l10n.galleryOpeningHours),
              KhadraCard(
                child: Column(
                  children: [
                    for (final day in gallery.operatingHours)
                      KhadraDetailRow(
                        dense: true,
                        label: _dayName(context, day.day),
                        value: Text(
                          day.isClosed
                              ? l10n.galleryClosed
                              : '${formats.clock(day.opens)} – ${formats.clock(day.closes)}',
                          style: TextStyle(
                            fontSize: 14,
                            fontWeight: FontWeight.w600,
                            color: day.isClosed
                                ? KhadraColors.neutral500
                                : KhadraColors.text,
                          ),
                        ),
                      ),
                  ],
                ),
              ),

              const SizedBox(height: Space.xl),
              KhadraSectionTitle(
                l10n.galleryLocation,
                trailing: TextButton.icon(
                  onPressed: () => _openInMaps(gallery),
                  icon: const Icon(Icons.open_in_new, size: 16),
                  label: Text(l10n.galleryOpenInMaps),
                ),
              ),
              _MapPin(
                latitude: gallery.latitude,
                longitude: gallery.longitude,
                label: gallery.businessName,
              ),

              const SizedBox(height: Space.xl),
              KhadraSectionTitle(
                l10n.reviewsTitle,
                trailing: gallery.reviewCount == 0
                    ? null
                    : TextButton(
                        onPressed: () => context
                            .push(Routes.galleryReviews(gallery.dealerId)),
                        child: Text(l10n.actionSeeAll),
                      ),
              ),
              if (gallery.reviewCount == 0)
                KhadraNotice(
                  title: l10n.reviewsEmpty,
                  tone: NoticeTone.neutral,
                  icon: Icons.star_outline_rounded,
                )
              else
                KhadraCard(
                  onTap: () =>
                      context.push(Routes.galleryReviews(gallery.dealerId)),
                  child: Row(
                    children: [
                      KhadraStars(rating: gallery.averageRating, size: 20),
                      const SizedBox(width: Space.md),
                      Expanded(
                        child: Text(
                          l10n.galleryReviewCount(gallery.reviewCount),
                          style: const TextStyle(
                              color: KhadraColors.neutral600, fontSize: 14),
                        ),
                      ),
                      const Icon(Icons.chevron_right,
                          color: KhadraColors.neutral400),
                    ],
                  ),
                ),

              const SizedBox(height: Space.xl),
              KhadraSectionTitle(l10n.galleryCars),
            ],
          ),
        ),

        switch (vehicles) {
          AsyncLoading() => const KhadraLoading(compact: true),
          AsyncError(:final error) => Padding(
              padding: const EdgeInsets.symmetric(horizontal: Space.lg),
              child: KhadraError(
                message: ApiFailure.from(error).messageFor(l10n),
                onRetry: () =>
                    ref.invalidate(galleryVehiclesProvider(gallery.dealerId)),
              ),
            ),
          AsyncData(:final value) when value.items.isEmpty => Padding(
              padding: const EdgeInsets.symmetric(horizontal: Space.lg),
              child: KhadraNotice(
                title: l10n.searchEmptyTitle,
                tone: NoticeTone.neutral,
              ),
            ),
          AsyncData(:final value) => Padding(
              padding: const EdgeInsets.symmetric(horizontal: Space.lg),
              child: Column(
                children: [
                  for (final listing in value.items) ...[
                    VehicleCard(listing: listing),
                    const SizedBox(height: Space.lg),
                  ],
                ],
              ),
            ),
          _ => const SizedBox.shrink(),
        },
      ],
    );
  }

  /// The day name from the platform's own `DayOfWeek`, rendered by Flutter's
  /// locale data rather than a table of seven strings in the ARB files.
  String _dayName(BuildContext context, String day) {
    const order = <String>[
      'Monday',
      'Tuesday',
      'Wednesday',
      'Thursday',
      'Friday',
      'Saturday',
      'Sunday',
    ];
    final index = order.indexOf(day);
    if (index < 0) return day;
    return MaterialLocalizations.of(context)
        .formatFullDate(DateTime(2024, 1, 1 + index))
        .split(',')
        .first;
  }

  Future<void> _openInMaps(PublicGallery gallery) async {
    final uri = Uri.parse(
      'https://www.google.com/maps/search/?api=1&query=${gallery.latitude},${gallery.longitude}',
    );
    await launchUrl(uri, mode: LaunchMode.externalApplication);
  }
}

class _RatingLine extends StatelessWidget {
  const _RatingLine({required this.gallery});

  final PublicGallery gallery;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);

    if (gallery.averageRating == null || gallery.reviewCount == 0) {
      return Text(
        l10n.galleryNotRatedYet,
        style: const TextStyle(color: KhadraColors.neutral500, fontSize: 13),
      );
    }

    return Row(
      children: [
        KhadraStars(rating: gallery.averageRating, size: 15),
        const SizedBox(width: Space.xs),
        LatinRun(
          gallery.averageRating!.toStringAsFixed(1),
          style: const TextStyle(fontSize: 13, fontWeight: FontWeight.w700),
        ),
        const SizedBox(width: Space.xs),
        Text(
          '· ${l10n.galleryReviewCount(gallery.reviewCount)}',
          style: const TextStyle(color: KhadraColors.neutral600, fontSize: 13),
        ),
      ],
    );
  }
}

/// A small non-interactive map.
///
/// Raster tiles from OpenStreetMap: no API key, no billing account, and nothing
/// about the customer leaves the app beyond the tile request itself. Tapping opens
/// the platform's own maps app, which is where somebody actually wants to navigate
/// from.
class _MapPin extends StatelessWidget {
  const _MapPin({
    required this.latitude,
    required this.longitude,
    required this.label,
  });

  final double latitude;
  final double longitude;
  final String label;

  @override
  Widget build(BuildContext context) => ClipRRect(
        borderRadius: Radii.card,
        child: SizedBox(
          height: 180,
          child: FlutterMap(
            options: MapOptions(
              initialCenter: LatLng(latitude, longitude),
              initialZoom: 14,
              interactionOptions: const InteractionOptions(
                flags: InteractiveFlag.none,
              ),
            ),
            children: [
              TileLayer(
                urlTemplate: 'https://tile.openstreetmap.org/{z}/{x}/{y}.png',
                userAgentPackageName: 'com.khadra.khadra_mobile',
              ),
              MarkerLayer(
                markers: [
                  Marker(
                    point: LatLng(latitude, longitude),
                    width: 40,
                    height: 40,
                    child: const Icon(
                      Icons.location_on,
                      color: KhadraColors.accent,
                      size: 36,
                    ),
                  ),
                ],
              ),
            ],
          ),
        ),
      );
}
