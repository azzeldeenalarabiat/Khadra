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
import '../auth/auth_form_widgets.dart';
import 'date_range_sheet.dart';
import 'search_providers.dart';
import 'vehicle_card.dart';

/// One car in full, with the gallery behind it.
///
/// The availability line is the SERVER's: `isAvailable` is null when no dates were
/// named, because "is it free" has no answer without a period to ask about, and
/// false would be a lie. The screen renders all three states rather than
/// collapsing null into "no".
class VehicleScreen extends ConsumerWidget {
  const VehicleScreen({super.key, required this.vehicleId});

  final String vehicleId;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);
    final filter = ref.watch(searchFilterProvider);
    final formats = ref.watch(formatsProvider);

    final vehicle = ref.watch(vehicleProvider((
      id: vehicleId,
      from: filter.hasDates ? filter.pickupAt : null,
      to: filter.hasDates ? filter.returnAt : null,
    )));

    return Scaffold(
      body: switch (vehicle) {
        AsyncLoading() => const _VehicleScaffold(child: KhadraLoading()),
        AsyncError(:final error) => _VehicleScaffold(
            child: _vehicleError(context, l10n, ApiFailure.from(error)),
          ),
        AsyncData(:final value) when formats != null =>
          _VehicleBody(vehicle: value, formats: formats),
        _ => const _VehicleScaffold(child: KhadraLoading()),
      },
      bottomNavigationBar: switch (vehicle) {
        AsyncData(:final value) when formats != null =>
          _BookingBar(vehicle: value, formats: formats),
        _ => null,
      },
    );
  }

  Widget _vehicleError(
      BuildContext context, AppLocalizations l10n, ApiFailure failure) {
    // 404 covers every reason at once — no such car, a draft, a hidden one, one
    // whose gallery is suspended. The API answers the same to all of them so an
    // anonymous caller cannot enumerate a competitor's unpublished inventory, and
    // the app must not pretend to know which it was.
    if (failure.isNotFound) {
      return KhadraEmpty(
        icon: Icons.no_transfer_outlined,
        title: l10n.vehicleNotFoundTitle,
        body: l10n.vehicleNotFoundBody,
        action: OutlinedButton(
          onPressed: () => context.go(Routes.search),
          child: Text(l10n.bookingsEmptyAction),
        ),
      );
    }
    return KhadraError(message: failure.messageFor(l10n));
  }
}

class _VehicleScaffold extends StatelessWidget {
  const _VehicleScaffold({required this.child});

  final Widget child;

  @override
  Widget build(BuildContext context) => SafeArea(
        child: Column(
          children: [
            const Align(
              alignment: AlignmentDirectional.centerStart,
              child: KhadraBack(fallback: Routes.search),
            ),
            Expanded(child: child),
          ],
        ),
      );
}

class _VehicleBody extends ConsumerWidget {
  const _VehicleBody({required this.vehicle, required this.formats});

  final CatalogueVehicle vehicle;
  final Formats formats;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);
    final arabic = ref.watch(isArabicProvider);
    final filter = ref.watch(searchFilterProvider);

    return CustomScrollView(
      slivers: [
        SliverAppBar(
          expandedHeight: 260,
          pinned: true,
          backgroundColor: KhadraColors.surface,
          leading: const KhadraBack(fallback: Routes.search),
          flexibleSpace: FlexibleSpaceBar(
            background: _Photos(urls: vehicle.imageUrls),
          ),
        ),
        SliverPadding(
          padding: const EdgeInsets.fromLTRB(Space.lg, Space.lg, Space.lg, Space.lg),
          sliver: SliverList.list(children: [
            Text(
              vehicle.title,
              style: const TextStyle(fontSize: 24, fontWeight: FontWeight.w700),
            ),
            const SizedBox(height: Space.xs),
            Text(
              [
                vehicle.year.toString(),
                if (vehicle.carType != null) vehicle.carType!.nameFor(arabic),
              ].join(' · '),
              style: const TextStyle(
                  color: KhadraColors.neutral600, fontSize: 14),
            ),
            const SizedBox(height: Space.lg),

            _AvailabilityLine(vehicle: vehicle, hasDates: filter.hasDates),

            if (vehicle.description != null &&
                vehicle.description!.trim().isNotEmpty) ...[
              const SizedBox(height: Space.xl),
              KhadraSectionTitle(l10n.vehicleAbout),
              Text(
                vehicle.description!,
                style: const TextStyle(fontSize: 15, height: 1.55),
              ),
            ],

            const SizedBox(height: Space.xl),
            KhadraSectionTitle(l10n.vehicleSpecifications),
            KhadraCard(
              child: Column(
                children: [
                  KhadraDetailRow(
                    label: l10n.searchTransmission,
                    value: Text(_vocabulary(
                        ref, (v) => v.transmissions, vehicle.transmission, arabic)),
                  ),
                  KhadraDetailRow(
                    label: l10n.vehicleFuel,
                    value: Text(_vocabulary(
                        ref, (v) => v.fuelTypes, vehicle.fuelType, arabic)),
                  ),
                  KhadraDetailRow(
                    label: l10n.searchSeats,
                    value: Text(l10n.vehicleSeats(vehicle.seats)),
                  ),
                  if (vehicle.color != null && vehicle.color!.isNotEmpty)
                    KhadraDetailRow(
                      label: l10n.vehicleColour,
                      value: Text(vehicle.color!),
                    ),
                ],
              ),
            ),

            const SizedBox(height: Space.xl),
            KhadraSectionTitle(l10n.vehicleMileage),
            KhadraCard(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    vehicle.mileage.isUnlimited
                        ? l10n.vehicleMileageUnlimited
                        : l10n.vehicleMileageLimited(
                            vehicle.mileage.dailyLimitKm ?? 0),
                    style: const TextStyle(
                        fontSize: 15, fontWeight: FontWeight.w600),
                  ),
                  if (!vehicle.mileage.isUnlimited &&
                      vehicle.mileage.excessFeePerKm != null) ...[
                    const SizedBox(height: Space.xs),
                    Text(
                      l10n.vehicleMileageExcess(
                          formats.money(vehicle.mileage.excessFeePerKm!)),
                      style: const TextStyle(
                          color: KhadraColors.neutral600, fontSize: 14),
                    ),
                  ],
                  const SizedBox(height: Space.md),
                  const Divider(height: 1),
                  const SizedBox(height: Space.md),
                  Text(
                    l10n.vehicleFuelPolicy,
                    style: const TextStyle(
                        color: KhadraColors.neutral600, fontSize: 13),
                  ),
                  const SizedBox(height: 2),
                  Text(
                    BookingPresentation.fuelPolicy(l10n, vehicle.fuelPolicy),
                    style: const TextStyle(fontSize: 14, height: 1.45),
                  ),
                ],
              ),
            ),

            const SizedBox(height: Space.xl),
            KhadraSectionTitle(l10n.vehicleSecurityDeposit),
            KhadraCard(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    formats.money(vehicle.securityDeposit),
                    style: const TextStyle(
                        fontSize: 18, fontWeight: FontWeight.w700),
                  ),
                  const SizedBox(height: Space.xs),
                  Text(
                    l10n.vehicleSecurityDepositHelp,
                    style: const TextStyle(
                        color: KhadraColors.neutral600,
                        fontSize: 13,
                        height: 1.45),
                  ),
                ],
              ),
            ),

            const SizedBox(height: Space.xl),
            KhadraSectionTitle(
              l10n.galleryTitle,
              trailing: TextButton(
                onPressed: () =>
                    context.push(Routes.gallery(vehicle.gallery.dealerId)),
                child: Text(l10n.actionSeeAll),
              ),
            ),
            _GallerySummary(gallery: vehicle.gallery, formats: formats),

            const SizedBox(height: Space.bottomInset),
          ]),
        ),
      ],
    );
  }

  /// The label for a platform member, in the reader's language, from the server's
  /// own vocabulary. A table in the app would be a second copy that could only
  /// drift from it.
  String _vocabulary(
    WidgetRef ref,
    List<VocabularyEntry> Function(Vocabularies) pick,
    String name,
    bool arabic,
  ) {
    final vocabularies = ref.watch(appConfigProvider).valueOrNull?.vocabularies;
    if (vocabularies == null) return name;
    return Vocabularies.label(pick(vocabularies), name, arabic);
  }
}

class _Photos extends StatefulWidget {
  const _Photos({required this.urls});

  final List<String> urls;

  @override
  State<_Photos> createState() => _PhotosState();
}

class _PhotosState extends State<_Photos> {
  final _controller = PageController();
  int _index = 0;

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    if (widget.urls.isEmpty) {
      return const KhadraImage(url: null);
    }

    return Stack(
      alignment: AlignmentDirectional.bottomCenter,
      children: [
        PageView.builder(
          controller: _controller,
          itemCount: widget.urls.length,
          onPageChanged: (index) => setState(() => _index = index),
          itemBuilder: (_, index) => KhadraImage(url: widget.urls[index]),
        ),
        if (widget.urls.length > 1)
          Padding(
            padding: const EdgeInsets.only(bottom: Space.md),
            child: Row(
              mainAxisSize: MainAxisSize.min,
              children: List.generate(
                widget.urls.length,
                (index) => Container(
                  width: 7,
                  height: 7,
                  margin: const EdgeInsets.symmetric(horizontal: 3),
                  decoration: BoxDecoration(
                    shape: BoxShape.circle,
                    color: index == _index
                        ? Colors.white
                        : Colors.white.withValues(alpha: 0.5),
                  ),
                ),
              ),
            ),
          ),
      ],
    );
  }
}

/// Three states, not two.
class _AvailabilityLine extends StatelessWidget {
  const _AvailabilityLine({required this.vehicle, required this.hasDates});

  final CatalogueVehicle vehicle;
  final bool hasDates;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);

    if (!hasDates || vehicle.isAvailable == null) {
      return KhadraNotice(
        title: l10n.vehicleChooseDatesToBook,
        tone: NoticeTone.neutral,
        icon: Icons.date_range_outlined,
      );
    }

    return vehicle.isAvailable!
        ? KhadraNotice(
            title: l10n.vehicleAvailableForDates,
            tone: NoticeTone.accent,
          )
        : KhadraNotice(
            title: l10n.vehicleUnavailableForDates,
            tone: NoticeTone.warn,
            icon: Icons.event_busy_outlined,
          );
  }
}

class _GallerySummary extends StatelessWidget {
  const _GallerySummary({required this.gallery, required this.formats});

  final PublicGallery gallery;
  final Formats formats;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);

    return KhadraCard(
      onTap: () => context.push(Routes.gallery(gallery.dealerId)),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              if (gallery.logoUrl != null)
                Padding(
                  padding: const EdgeInsetsDirectional.only(end: Space.md),
                  child: SizedBox(
                    width: 44,
                    height: 44,
                    child: KhadraImage(
                      url: gallery.logoUrl,
                      fit: BoxFit.contain,
                      borderRadius: const BorderRadius.all(Radii.sm),
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
                          fontSize: 16, fontWeight: FontWeight.w700),
                    ),
                    const SizedBox(height: 2),
                    if (gallery.averageRating != null && gallery.reviewCount > 0)
                      Row(
                        children: [
                          KhadraStars(rating: gallery.averageRating, size: 14),
                          const SizedBox(width: Space.xs),
                          Text(
                            l10n.galleryReviewCount(gallery.reviewCount),
                            style: const TextStyle(
                                color: KhadraColors.neutral600, fontSize: 12),
                          ),
                        ],
                      )
                    else
                      Text(
                        l10n.galleryNotRatedYet,
                        style: const TextStyle(
                            color: KhadraColors.neutral500, fontSize: 12),
                      ),
                  ],
                ),
              ),
              const Icon(Icons.chevron_right, color: KhadraColors.neutral400),
            ],
          ),
          const SizedBox(height: Space.md),
          const Divider(height: 1),
          const SizedBox(height: Space.md),
          Text(
            // The fee is this GALLERY's own figure. There is no platform-wide
            // delivery price to fall back on, so when delivery is off the app
            // says so rather than inventing one.
            gallery.delivery.isEnabled && gallery.delivery.fee != null
                ? l10n.galleryDeliveryOffered(
                    gallery.delivery.radiusKm.toString(),
                    formats.money(gallery.delivery.fee!),
                  )
                : l10n.galleryDeliveryNotOffered,
            style: const TextStyle(
                fontSize: 13, height: 1.45, color: KhadraColors.neutral700),
          ),
        ],
      ),
    );
  }
}

/// The sticky bar: the price, and the one action.
class _BookingBar extends ConsumerWidget {
  const _BookingBar({required this.vehicle, required this.formats});

  final CatalogueVehicle vehicle;
  final Formats formats;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);
    final filter = ref.watch(searchFilterProvider);
    final unavailable = filter.hasDates && vehicle.isAvailable == false;

    return Container(
      decoration: const BoxDecoration(
        color: KhadraColors.surface,
        border: Border(top: BorderSide(color: KhadraColors.neutral200)),
      ),
      child: SafeArea(
        child: Padding(
          padding: const EdgeInsets.all(Space.lg),
          child: Row(
            children: [
              DailyRateLabel(formats: formats, rate: vehicle.dailyRate),
              const SizedBox(width: Space.lg),
              Expanded(
                child: KhadraSubmitButton(
                  label: unavailable
                      ? l10n.searchChooseDates
                      : filter.hasDates
                          ? l10n.vehicleSeePrice
                          : l10n.searchChooseDates,
                  onPressed: () async {
                    // Without dates there is nothing to price, so the dates come
                    // first. The quote screen is never reached on a guess.
                    if (!filter.hasDates || unavailable) {
                      final chosen = await showDateRangeSheet(
                        context: context,
                        ref: ref,
                        initial: filter.hasDates
                            ? ChosenDates(filter.pickupAt!, filter.returnAt!)
                            : null,
                      );
                      if (chosen == null) return;
                      ref.read(searchFilterProvider.notifier).update(
                            (value) => value.copyWith(
                              pickupAt: chosen.pickupAt,
                              returnAt: chosen.returnAt,
                            ),
                          );
                      return;
                    }

                    if (context.mounted) {
                      context.push(Routes.requestBooking(vehicle.vehicleId));
                    }
                  },
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
