import 'dart:async';

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
import '../reviews/reviews_screen.dart';
import '../shortlist/shortlist_providers.dart';
import 'search_providers.dart';
import 'vehicle_row.dart';

/// A rental office's public page.
///
/// What is NOT here matters as much as what is: no commercial registration number,
/// no verification status, no suspension reason, and no badge of any kind. A
/// gallery that cannot trade is not returned by the API at all, so a status field
/// would only ever read "approved" and invite somebody to add the others beside it.
///
/// The page has two halves and the difference is the point. What the PLATFORM knows
/// — the opening hours a pickup is held to, whether delivery is offered and at what
/// price, where the office is, its rating, its cars — is always here, and the office
/// cannot hide any of it. What the office WROTE is shown under its own name, in the
/// office's words, and only where it wrote something and chose to show it. A section
/// the server sends as null renders nothing at all: hidden and never-written are the
/// same silence, deliberately, so the page cannot become a way to ask what an office
/// is keeping back.
class GalleryScreen extends ConsumerWidget {
  const GalleryScreen({super.key, required this.dealerId});

  final String dealerId;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);
    final gallery = ref.watch(galleryProvider(dealerId));
    final formats = ref.watch(formatsProvider);

    return Scaffold(
      appBar: AppBar(
        leading: const KhadraBack(fallback: Routes.search),
        title: Text(l10n.galleryTitle),
      ),
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

class _GalleryBody extends ConsumerStatefulWidget {
  const _GalleryBody({required this.gallery, required this.formats});

  final PublicGalleryPage gallery;
  final Formats formats;

  @override
  ConsumerState<_GalleryBody> createState() => _GalleryBodyState();
}

class _GalleryBodyState extends ConsumerState<_GalleryBody> {
  final _cars = GlobalKey();
  final _reviews = GlobalKey();

  Future<void> _scrollTo(GlobalKey key) async {
    final target = key.currentContext;
    if (target == null) return;
    await Scrollable.ensureVisible(
      target,
      duration: const Duration(milliseconds: 250),
      curve: Curves.easeOut,
      alignment: 0.05,
    );
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final gallery = widget.gallery;
    final formats = widget.formats;
    final sections = gallery.sections;
    final vehicles = ref.watch(galleryVehiclesProvider(gallery.dealerId));

    return ListView(
      padding: const EdgeInsets.only(bottom: Space.bottomInset),
      children: [
        _Header(gallery: gallery),

        Padding(
          padding: const EdgeInsets.fromLTRB(Space.lg, Space.lg, Space.lg, 0),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              _ActionRow(
                onMaps: () => _openInMaps(gallery),
                onCars: () => _scrollTo(_cars),
                onReviews: () => _scrollTo(_reviews),
              ),

              // The office's own description. Long ones are folded, because the
              // hours and the delivery terms below it are what a customer came for.
              if (sections.about case final about?) ...[
                const SizedBox(height: Space.xl),
                KhadraSectionTitle(l10n.galleryAbout),
                _FoldedText(about),
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
              // Only ever sent while delivery is on: the server does not describe a
              // service the office is not offering.
              if (sections.deliveryNotes case final notes?) ...[
                const SizedBox(height: Space.sm),
                _OfficeText(notes),
              ],

              if (sections.pickupInstructions case final pickup?) ...[
                const SizedBox(height: Space.xl),
                KhadraSectionTitle(l10n.galleryPickupInstructions),
                _OfficeText(pickup),
              ],

              const SizedBox(height: Space.xl),
              _OpeningHours(hours: gallery.operatingHours, formats: formats),

              const SizedBox(height: Space.xl),
              KhadraSectionTitle(
                l10n.galleryLocation,
                trailing: TextButton.icon(
                  onPressed: () => _openInMaps(gallery),
                  icon: const Icon(Icons.open_in_new, size: 16),
                  label: Text(l10n.galleryOpenInMaps),
                ),
              ),
              if (_addressLine(gallery) case final address?)
                Padding(
                  padding: const EdgeInsets.only(bottom: Space.sm),
                  child: UserText(
                    address,
                    style: const TextStyle(
                        color: KhadraColors.neutral700, fontSize: 14),
                  ),
                ),
              _MapPin(
                latitude: gallery.latitude,
                longitude: gallery.longitude,
                label: gallery.businessName,
              ),

              // Everything the office says about renting from it, under one heading
              // that says whose words these are.
              if (sections.rentalConditions != null ||
                  sections.insurance != null ||
                  sections.customerNotes != null) ...[
                const SizedBox(height: Space.xl),
                KhadraSectionTitle(l10n.galleryFromTheOffice),
                Padding(
                  padding: const EdgeInsets.only(bottom: Space.sm),
                  child: Text(
                    l10n.galleryOfficeOwnWords,
                    style: const TextStyle(
                        color: KhadraColors.neutral500, fontSize: 12),
                  ),
                ),
                if (sections.rentalConditions case final conditions?)
                  _OfficeSection(title: l10n.galleryRentalConditions, text: conditions),
                if (sections.insurance case final insurance?)
                  _OfficeSection(title: l10n.galleryInsurance, text: insurance),
                if (sections.customerNotes case final notes?)
                  _OfficeSection(title: l10n.galleryNotes, text: notes),
              ],

              const SizedBox(height: Space.xl),
              KhadraSectionTitle(
                l10n.reviewsTitle,
                key: _reviews,
                trailing: gallery.reviewCount == 0
                    ? null
                    : TextButton(
                        onPressed: () => context
                            .push(Routes.galleryReviews(gallery.dealerId)),
                        child: Text(l10n.actionSeeAll),
                      ),
              ),
              _Reviews(gallery: gallery, formats: formats),

              const SizedBox(height: Space.xl),
              KhadraSectionTitle(l10n.galleryCars, key: _cars),
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
          AsyncData(:final value) => _GalleryVehicles(listings: value.items),
          _ => const SizedBox.shrink(),
        },
      ],
    );
  }

  Future<void> _openInMaps(PublicGalleryPage gallery) async {
    final uri = Uri.parse(
      'https://www.google.com/maps/search/?api=1&query=${gallery.latitude},${gallery.longitude}',
    );
    await launchUrl(uri, mode: LaunchMode.externalApplication);
  }
}

/// The office's address in words, or null. Joined with a middle dot rather than a
/// sentence, so it reads the same in both languages and needs no translation.
String? _addressLine(PublicGalleryPage gallery) {
  final address = gallery.address;
  if (address == null || address.area.trim().isEmpty) return null;

  final street = address.street;
  return street == null || street.trim().isEmpty
      ? address.area
      : '${address.area} · $street';
}

/// The cover, the mark and who this is.
class _Header extends ConsumerWidget {
  const _Header({required this.gallery});

  final PublicGalleryPage gallery;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);
    final city = ref.watch(cityNameProvider(gallery.cityId));
    // The AREA, not the full street. Roughly where this office is, is what somebody
    // reads beside its name; exactly where it is belongs on the location card with
    // the map, and a long street name repeated up here only wraps.
    //
    // Each part isolated before they are joined: the city is in the reader's
    // language and the area is in whichever language the office typed it in, and an
    // Arabic city beside a Latin area reorders into nonsense unless each run is
    // fenced off from the other.
    final where = [city, gallery.address?.area]
        .whereType<String>()
        .where((part) => part.trim().isNotEmpty)
        .map(Formats.isolate)
        .join(' · ');

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        if (gallery.coverUrl != null)
          Stack(
            children: [
              AspectRatio(
                aspectRatio: 16 / 7,
                child: KhadraImage(url: gallery.coverUrl),
              ),
              // A scrim, so a pale photograph cannot leave the mark below it
              // floating on nothing.
              const Positioned.fill(
                child: DecoratedBox(
                  decoration: BoxDecoration(gradient: KhadraGradients.coverScrim),
                ),
              ),
            ],
          ),
        Padding(
          padding: const EdgeInsets.fromLTRB(Space.lg, Space.lg, Space.lg, 0),
          child: Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              if (gallery.logoUrl != null)
                Padding(
                  padding: const EdgeInsetsDirectional.only(end: Space.md),
                  child: SizedBox(
                    width: 64,
                    height: 64,
                    child: KhadraImage(
                      url: gallery.logoUrl,
                      fit: BoxFit.contain,
                      borderRadius: Radii.field,
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
                          fontSize: 21, fontWeight: FontWeight.w800),
                    ),
                    if (where.isNotEmpty) ...[
                      const SizedBox(height: 2),
                      Row(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          const Padding(
                            padding: EdgeInsets.only(top: 2),
                            child: Icon(Icons.place_outlined,
                                size: 14, color: KhadraColors.neutral500),
                          ),
                          const SizedBox(width: 4),
                          Expanded(
                            child: Text(
                              where,
                              style: const TextStyle(
                                  color: KhadraColors.neutral600, fontSize: 13),
                              maxLines: 2,
                              overflow: TextOverflow.ellipsis,
                            ),
                          ),
                        ],
                      ),
                    ],
                    const SizedBox(height: Space.xs),
                    _RatingLine(
                      rating: gallery.averageRating,
                      reviewCount: gallery.reviewCount,
                      notRatedYet: l10n.galleryNotRatedYet,
                    ),
                  ],
                ),
              ),
            ],
          ),
        ),
      ],
    );
  }
}

/// The three things somebody opens this page to do.
class _ActionRow extends StatelessWidget {
  const _ActionRow({
    required this.onMaps,
    required this.onCars,
    required this.onReviews,
  });

  final VoidCallback onMaps;
  final VoidCallback onCars;
  final VoidCallback onReviews;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);

    // Sized to fit all three across a 360 phone at the ordinary text size, and
    // allowed to scroll rather than squeeze above it: at a large system font
    // three of these cannot fit any phone, and a clipped word is a better signal
    // that there is more than a label shrunk until it cannot be read.
    return SingleChildScrollView(
      scrollDirection: Axis.horizontal,
      child: Row(
        children: [
          _Action(icon: Icons.map_outlined, label: l10n.galleryOpenInMaps, onTap: onMaps),
          const SizedBox(width: Space.sm),
          _Action(icon: Icons.directions_car_outlined, label: l10n.galleryCars, onTap: onCars),
          const SizedBox(width: Space.sm),
          _Action(icon: Icons.star_outline_rounded, label: l10n.reviewsTitle, onTap: onReviews),
        ],
      ),
    );
  }
}

class _Action extends StatelessWidget {
  const _Action({required this.icon, required this.label, required this.onTap});

  final IconData icon;
  final String label;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) => OutlinedButton.icon(
        onPressed: onTap,
        style: OutlinedButton.styleFrom(
          minimumSize: const Size(0, 44),
          padding: const EdgeInsets.symmetric(horizontal: Space.sm),
        ),
        icon: Icon(icon, size: 16),
        // A size MERGED onto the button's own style, never a style of its own:
        // `styleFrom(textStyle:)` replaces it outright and takes the type family
        // with it, which is how these came out in the system font.
        label: Text(label, style: const TextStyle(fontSize: 13)),
      );
}

/// A long piece of the office's own writing, folded to four lines until asked.
class _FoldedText extends StatefulWidget {
  const _FoldedText(this.text);

  final ResolvedText text;

  @override
  State<_FoldedText> createState() => _FoldedTextState();
}

class _FoldedTextState extends State<_FoldedText> {
  static const _foldedLines = 4;
  bool _open = false;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);

    return LayoutBuilder(
      builder: (context, constraints) {
        final folds = _longerThanTheFold(context, constraints.maxWidth);

        return Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            _OfficeText(
              widget.text,
              maxLines: _open || !folds ? null : _foldedLines,
            ),
            if (folds)
              Align(
                alignment: AlignmentDirectional.centerStart,
                child: TextButton(
                  onPressed: () => setState(() => _open = !_open),
                  child: Text(_open ? l10n.actionShowLess : l10n.actionShowMore),
                ),
              ),
          ],
        );
      },
    );
  }

  /// Whether there is anything to unfold.
  ///
  /// Measured at the width, the text size and the direction it will actually be
  /// drawn at, because all three decide it: an office's three lines of English
  /// become five in Arabic, or at a large system font, and a Show more that opens
  /// nothing is worse than no Show more at all.
  bool _longerThanTheFold(BuildContext context, double width) {
    if (!width.isFinite) return true;

    final painter = TextPainter(
      text: TextSpan(
        text: widget.text.text,
        style: DefaultTextStyle.of(context).style.merge(_OfficeText.style),
      ),
      maxLines: _foldedLines,
      textDirection: UserText.directionOf(widget.text.text),
      textScaler: MediaQuery.textScalerOf(context),
    )..layout(maxWidth: width);

    return painter.didExceedMaxLines;
  }
}

/// Text somebody at the office typed: rendered in ITS direction, not the app's.
class _OfficeText extends StatelessWidget {
  const _OfficeText(this.text, {this.maxLines});

  /// Public so the fold above can measure exactly what it will render.
  static const TextStyle style = TextStyle(fontSize: 15, height: 1.55);

  final ResolvedText text;
  final int? maxLines;

  @override
  Widget build(BuildContext context) =>
      UserText.resolved(text, maxLines: maxLines, style: style);
}

class _OfficeSection extends StatelessWidget {
  const _OfficeSection({required this.title, required this.text});

  final String title;
  final ResolvedText text;

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.only(bottom: Space.md),
        child: KhadraCard(
          child: Column(
            // Stretch, not start: a card sized to how far its own sentence happened
            // to wrap comes out narrower than the two above it, and three cards of
            // three widths read as three different kinds of thing.
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Text(
                title,
                style: const TextStyle(fontSize: 14, fontWeight: FontWeight.w700),
              ),
              const SizedBox(height: Space.xs),
              _OfficeText(text),
            ],
          ),
        ),
      );
}

/// Today first, the week on request.
///
/// A customer standing outside at six in the evening wants one line, and the line
/// they want is about TODAY in Amman — which is the zone every pickup is judged in,
/// not the zone the phone happens to be in.
class _OpeningHours extends ConsumerStatefulWidget {
  const _OpeningHours({required this.hours, required this.formats});

  final List<GalleryDaySchedule> hours;
  final Formats formats;

  @override
  ConsumerState<_OpeningHours> createState() => _OpeningHoursState();
}

class _OpeningHoursState extends ConsumerState<_OpeningHours> {
  bool _open = false;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final formats = widget.formats;
    final today = _today();

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        KhadraSectionTitle(
          l10n.galleryOpeningHours,
          // "All week" rather than "Show more": what it opens is a schedule, and
          // the About fold further up this same page already owns those words.
          trailing: TextButton(
            onPressed: () => setState(() => _open = !_open),
            child: Text(_open ? l10n.galleryTodayOnly : l10n.galleryAllWeek),
          ),
        ),
        KhadraCard(
          child: Column(
            children: [
              // Collapsed, today is a SENTENCE rather than a label and a value:
              // "Open today 8:00 AM – 8:00 PM" is the whole answer somebody
              // standing outside the office wants, and naming the weekday beside
              // it only repeats the word today.
              if (!_open && today != null)
                Row(
                  children: [
                    Icon(
                      today.isClosed
                          ? Icons.schedule_outlined
                          : Icons.schedule_rounded,
                      size: 18,
                      color: today.isClosed
                          ? KhadraColors.neutral500
                          : KhadraColors.ok,
                    ),
                    const SizedBox(width: Space.sm),
                    Expanded(
                      child: Text(
                        today.isClosed
                            ? l10n.galleryClosedToday
                            : l10n.galleryOpenToday(formats.clock(today.opens),
                                formats.clock(today.closes)),
                        style: TextStyle(
                          fontSize: 14,
                          fontWeight: FontWeight.w700,
                          color: today.isClosed
                              ? KhadraColors.neutral500
                              : KhadraColors.ok,
                        ),
                      ),
                    ),
                  ],
                ),
              if (_open || today == null)
                for (final day in widget.hours)
                  KhadraDetailRow(
                    dense: true,
                    label: formats.weekday(day.day),
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
      ],
    );
  }

  /// Today's row, in AMMAN. Null when the office sent no schedule for it, and the
  /// whole week is shown instead rather than a day invented for it.
  GalleryDaySchedule? _today() {
    final name = widget.formats.weekdayInAmman(DateTime.now());

    for (final day in widget.hours) {
      if (day.day == name) return day;
    }
    return null;
  }
}

/// The score, and the three most recent things people said.
class _Reviews extends ConsumerWidget {
  const _Reviews({required this.gallery, required this.formats});

  final PublicGalleryPage gallery;
  final Formats formats;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);

    if (gallery.reviewCount == 0) {
      return KhadraNotice(
        title: l10n.reviewsEmpty,
        tone: NoticeTone.neutral,
        icon: Icons.star_outline_rounded,
      );
    }

    final latest = ref.watch(galleryReviewsProvider(gallery.dealerId)).valueOrNull;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        KhadraCard(
          onTap: () => context.push(Routes.galleryReviews(gallery.dealerId)),
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
              const KhadraDisclosure(),
            ],
          ),
        ),
        if (latest != null)
          for (final review in latest.items.take(3)) ...[
            const SizedBox(height: Space.md),
            ReviewCard(review: review, formats: formats),
          ],
      ],
    );
  }
}

class _RatingLine extends StatelessWidget {
  const _RatingLine({
    required this.rating,
    required this.reviewCount,
    required this.notRatedYet,
  });

  final num? rating;
  final int reviewCount;
  final String notRatedYet;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);

    if (rating == null || reviewCount == 0) {
      return Text(
        notRatedYet,
        style: const TextStyle(color: KhadraColors.neutral500, fontSize: 13),
      );
    }

    return Row(
      children: [
        KhadraStars(rating: rating, size: 15),
        const SizedBox(width: Space.xs),
        LatinRun(
          rating!.toStringAsFixed(1),
          style: const TextStyle(fontSize: 13, fontWeight: FontWeight.w700),
        ),
        const SizedBox(width: Space.xs),
        Flexible(
          child: Text(
            '· ${l10n.galleryReviewCount(reviewCount)}',
            style: const TextStyle(color: KhadraColors.neutral600, fontSize: 13),
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
          ),
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

/// The gallery's cars, with a heart on each.
///
/// A widget of its own only so the membership question can be asked once for the
/// whole set after the frame — writing to a provider while the tree that reads it
/// is being built is not allowed, and the alternative is one request per card.
class _GalleryVehicles extends ConsumerStatefulWidget {
  const _GalleryVehicles({required this.listings});

  final List<CatalogueListing> listings;

  @override
  ConsumerState<_GalleryVehicles> createState() => _GalleryVehiclesState();
}

class _GalleryVehiclesState extends ConsumerState<_GalleryVehicles> {
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted) return;
      unawaited(ref.read(savedVehiclesProvider.notifier).learn(
            [for (final listing in widget.listings) listing.vehicleId],
          ));
    });
  }

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.symmetric(horizontal: Space.lg),
        child: Column(
          children: [
            for (final listing in widget.listings) ...[
              VehicleRow(
                listing: listing,
                trailing: VehicleRowSaveButton(vehicleId: listing.vehicleId),
              ),
              const SizedBox(height: Space.lg),
            ],
          ],
        ),
      );
}
