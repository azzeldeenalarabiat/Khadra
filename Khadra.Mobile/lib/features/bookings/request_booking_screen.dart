import 'package:flutter/material.dart';
import 'package:flutter_map/flutter_map.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:latlong2/latlong.dart';

import '../../api/dtos.dart';
import '../../core/api/api_failure.dart';
import '../../core/api/api_failure_messages.dart';
import '../../core/format/formats.dart';
import '../../core/providers.dart';
import '../../core/router.dart';
import '../../core/theme/khadra_theme.dart';
import '../../core/widgets/khadra_widgets.dart';
import '../../l10n/app_localizations.dart';
import '../auth/auth_form_widgets.dart';
import '../catalogue/date_range_sheet.dart';
import '../catalogue/search_providers.dart';
import '../documents/document_providers.dart';
import 'booking_providers.dart';

/// The last screen before a request reaches a gallery.
///
/// **Every figure on it is the server's.** The app asks `/vehicles/{id}/quote` and
/// renders what comes back — the daily rate, the billed day count, the delivery
/// fee, the deposit, the cash due at the counter. It never multiplies, never
/// counts days, and sends no prices when it posts: a client that could name a
/// total could name a cheaper one.
///
/// The quote is re-requested whenever the pickup method or the delivery point
/// changes, because both change the price. That is also why the button is wired to
/// the quote's own `isAvailable` rather than to anything remembered from the
/// search: a customer must never see a price beside a button that gets refused.
class RequestBookingScreen extends ConsumerStatefulWidget {
  const RequestBookingScreen({super.key, required this.vehicleId});

  final String vehicleId;

  @override
  ConsumerState<RequestBookingScreen> createState() =>
      _RequestBookingScreenState();
}

class _RequestBookingScreenState extends ConsumerState<RequestBookingScreen> {
  static const _selfPickup = 'SelfPickup';
  static const _delivery = 'Delivery';

  String _pickupMethod = _selfPickup;
  LatLng? _deliveryPoint;
  bool _submitting = false;
  String? _error;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final filter = ref.watch(searchFilterProvider);
    final formats = ref.watch(formatsProvider);

    if (!filter.hasDates || formats == null) {
      // Reached by a deep link or a back-navigation that lost the dates. Nothing
      // can be priced without them, so the screen says so rather than showing an
      // empty summary.
      return Scaffold(
        appBar: AppBar(title: Text(l10n.bookTitle)),
        body: KhadraEmpty(
          icon: Icons.date_range_outlined,
          title: l10n.searchChooseDates,
          body: l10n.searchDatesHelp,
          action: OutlinedButton(
            onPressed: () => context.pop(),
            child: Text(l10n.actionBack),
          ),
        ),
      );
    }

    final vehicle = ref.watch(vehicleProvider((
      id: widget.vehicleId,
      from: filter.pickupAt,
      to: filter.returnAt,
    )));

    final quote = ref.watch(quoteProvider((
      vehicleId: widget.vehicleId,
      pickupAt: filter.pickupAt!,
      returnAt: filter.returnAt!,
      pickupMethod: _pickupMethod,
      latitude: _deliveryPoint?.latitude,
      longitude: _deliveryPoint?.longitude,
    )));

    return Scaffold(
      appBar: AppBar(title: Text(l10n.bookTitle)),
      body: switch (vehicle) {
        AsyncData(:final value) => _body(l10n, formats, value, quote),
        AsyncError(:final error) => KhadraError(
            message: ApiFailure.from(error).messageFor(l10n),
          ),
        _ => const KhadraLoading(),
      },
      bottomNavigationBar: switch ((vehicle, quote)) {
        (AsyncData(value: final car), AsyncData(value: final priced)) =>
          _SubmitBar(
            enabled: priced.isAvailable && !_needsDeliveryPoint,
            busy: _submitting,
            total: formats.money(priced.pricing.totalPrice),
            onSubmit: () => _submit(car, priced),
          ),
        _ => null,
      },
    );
  }

  bool get _needsDeliveryPoint =>
      _pickupMethod == _delivery && _deliveryPoint == null;

  Widget _body(
    AppLocalizations l10n,
    Formats formats,
    CatalogueVehicle vehicle,
    AsyncValue<RentalQuote> quote,
  ) {
    final filter = ref.watch(searchFilterProvider);
    final documents = ref.watch(myDocumentsProvider);
    final session = ref.watch(sessionProvider);

    return ListView(
      padding: const EdgeInsets.fromLTRB(
          Space.lg, Space.lg, Space.lg, Space.bottomInset),
      children: [
        _VehicleStrip(vehicle: vehicle, formats: formats),

        const SizedBox(height: Space.xl),
        KhadraSectionTitle(l10n.searchDates),
        KhadraCard(
          onTap: () async {
            final chosen = await showDateRangeSheet(
              context: context,
              ref: ref,
              initial: ChosenDates(filter.pickupAt!, filter.returnAt!),
            );
            if (chosen != null) {
              ref.read(searchFilterProvider.notifier).update(
                    (value) => value.copyWith(
                      pickupAt: chosen.pickupAt,
                      returnAt: chosen.returnAt,
                    ),
                  );
            }
          },
          child: Row(
            children: [
              const Icon(Icons.date_range_outlined,
                  color: KhadraColors.neutral600),
              const SizedBox(width: Space.md),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      formats.dateRange(filter.pickupAt!, filter.returnAt!),
                      style: const TextStyle(
                          fontSize: 15, fontWeight: FontWeight.w600),
                    ),
                    const SizedBox(height: 2),
                    Text(
                      '${formats.time(filter.pickupAt!)} – ${formats.time(filter.returnAt!)} · ${l10n.timeAmmanNote}',
                      style: const TextStyle(
                          color: KhadraColors.neutral600, fontSize: 12),
                    ),
                  ],
                ),
              ),
              const Icon(Icons.edit_outlined,
                  size: 18, color: KhadraColors.neutral400),
            ],
          ),
        ),

        const SizedBox(height: Space.xl),
        KhadraSectionTitle(l10n.bookPickupMethod),
        _PickupMethodChoice(
          value: _pickupMethod,
          // The gallery must offer delivery AND this car must be eligible for it.
          // Both halves come from the server; offering the option without them
          // would produce a quote the API refuses.
          deliveryOffered:
              vehicle.gallery.delivery.isEnabled && vehicle.isDeliveryEligible,
          deliveryFee: vehicle.gallery.delivery.fee,
          formats: formats,
          onChanged: (method) => setState(() {
            _pickupMethod = method;
            if (method == _selfPickup) _deliveryPoint = null;
          }),
        ),

        if (_pickupMethod == _delivery) ...[
          const SizedBox(height: Space.lg),
          _DeliveryPointPicker(
            gallery: vehicle.gallery,
            chosen: _deliveryPoint,
            onChanged: (point) => setState(() => _deliveryPoint = point),
          ),
        ],

        const SizedBox(height: Space.xl),
        KhadraSectionTitle(l10n.bookPriceTitle),
        switch (quote) {
          AsyncLoading() => const KhadraLoading(compact: true),
          AsyncError(:final error) => _quoteProblem(l10n, ApiFailure.from(error)),
          AsyncData(:final value) => _PriceBreakdown(
              quote: value,
              formats: formats,
              securityDeposit: vehicle.securityDeposit,
            ),
          _ => const SizedBox.shrink(),
        },

        if (quote.valueOrNull case final priced?) ...[
          const SizedBox(height: Space.xl),
          KhadraSectionTitle(l10n.bookTermsTitle),
          _Terms(quote: priced, formats: formats),
        ],

        // Read BEFORE the button rather than discovered by being refused. The
        // server demands both documents and a verified email; a customer who
        // taps and is turned away has wasted the tap and learned nothing they
        // could have been told first.
        if (session.isSignedIn) ...[
          if (documents.valueOrNull case final papers?
              when !papers.isComplete) ...[
            const SizedBox(height: Space.xl),
            KhadraNotice(
              title: l10n.bookDocumentsNeededTitle,
              body: l10n.bookDocumentsNeededBody,
              tone: NoticeTone.warn,
              icon: Icons.badge_outlined,
              action: OutlinedButton(
                onPressed: () => context.push(Routes.documents),
                child: Text(l10n.bookDocumentsNeededAction),
              ),
            ),
          ],
          if (session.user?.isEmailVerified == false) ...[
            const SizedBox(height: Space.lg),
            KhadraNotice(
              title: l10n.bookVerifyEmailFirst,
              tone: NoticeTone.warn,
              icon: Icons.mark_email_unread_outlined,
              action: OutlinedButton(
                onPressed: () => context.push(
                  Uri(
                    path: Routes.verifyEmail,
                    queryParameters: {'email': session.user!.email},
                  ).toString(),
                ),
                child: Text(l10n.authResendVerification),
              ),
            ),
          ],
        ],

        if (_error != null) ...[
          const SizedBox(height: Space.lg),
          KhadraNotice(title: _error!, tone: NoticeTone.bad),
        ],
      ],
    );
  }

  Widget _quoteProblem(AppLocalizations l10n, ApiFailure failure) =>
      KhadraNotice(
        title: failure.messageFor(l10n),
        tone: NoticeTone.bad,
      );

  Future<void> _submit(CatalogueVehicle vehicle, RentalQuote quote) async {
    final l10n = AppLocalizations.of(context);
    final filter = ref.read(searchFilterProvider);

    setState(() {
      _submitting = true;
      _error = null;
    });

    try {
      final booking = await ref.read(apiProvider).createBooking(
            vehicleId: widget.vehicleId,
            pickupAt: filter.pickupAt!,
            returnAt: filter.returnAt!,
            pickupMethod: _pickupMethod,
            latitude: _deliveryPoint?.latitude,
            longitude: _deliveryPoint?.longitude,
          );

      invalidateBookings(ref);

      if (!mounted) return;
      await showDialog<void>(
        context: context,
        barrierDismissible: false,
        builder: (_) => _RequestSentDialog(
          booking: booking,
          galleryName: vehicle.gallery.businessName,
        ),
      );

      if (!mounted) return;
      context.go(Routes.booking(booking.bookingId));
    } on ApiFailure catch (failure) {
      if (!mounted) return;

      // Somebody took the car while the customer was deciding. Re-reading the
      // vehicle is what turns the sticky bar from "request" into "not free for
      // those dates" rather than leaving a button that will fail again.
      if (failure.hasCode('booking.vehicle_unavailable')) {
        ref.invalidate(vehicleProvider((
          id: widget.vehicleId,
          from: filter.pickupAt,
          to: filter.returnAt,
        )));
      }

      setState(() {
        _submitting = false;
        _error = failure.messageFor(l10n);
      });
    }
  }
}

class _VehicleStrip extends StatelessWidget {
  const _VehicleStrip({required this.vehicle, required this.formats});

  final CatalogueVehicle vehicle;
  final Formats formats;

  @override
  Widget build(BuildContext context) => KhadraCard(
        padding: const EdgeInsets.all(Space.md),
        child: Row(
          children: [
            SizedBox(
              width: 84,
              height: 64,
              child: KhadraImage(
                url: vehicle.imageUrls.isEmpty ? null : vehicle.imageUrls.first,
                borderRadius: const BorderRadius.all(Radii.md),
              ),
            ),
            const SizedBox(width: Space.md),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    vehicle.title,
                    style: const TextStyle(
                        fontSize: 16, fontWeight: FontWeight.w700),
                  ),
                  const SizedBox(height: 2),
                  Text(
                    vehicle.gallery.businessName,
                    style: const TextStyle(
                        color: KhadraColors.neutral600, fontSize: 13),
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                  ),
                ],
              ),
            ),
          ],
        ),
      );
}

class _PickupMethodChoice extends ConsumerWidget {
  const _PickupMethodChoice({
    required this.value,
    required this.deliveryOffered,
    required this.deliveryFee,
    required this.formats,
    required this.onChanged,
  });

  final String value;
  final bool deliveryOffered;
  final Money? deliveryFee;
  final Formats formats;
  final ValueChanged<String> onChanged;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);
    final arabic = ref.watch(isArabicProvider);
    final methods =
        ref.watch(appConfigProvider).valueOrNull?.vocabularies.pickupMethods ??
            const <VocabularyEntry>[];

    return Column(
      children: [
        for (final method in methods)
          if (method.name != 'Delivery' || deliveryOffered)
            Padding(
              padding: const EdgeInsets.only(bottom: Space.sm),
              child: KhadraCard(
                onTap: () => onChanged(method.name),
                borderColor: value == method.name
                    ? KhadraColors.accent
                    : KhadraColors.neutral200,
                background: value == method.name
                    ? KhadraColors.accent100
                    : KhadraColors.surface,
                child: Row(
                  children: [
                    Icon(
                      value == method.name
                          ? Icons.radio_button_checked
                          : Icons.radio_button_off,
                      color: value == method.name
                          ? KhadraColors.accent
                          : KhadraColors.neutral400,
                      size: 20,
                    ),
                    const SizedBox(width: Space.md),
                    Expanded(
                      // The words are the PLATFORM's, in both languages, from
                      // /app-config. Not a chip's text typed into a widget.
                      child: Text(
                        method.labelFor(arabic),
                        style: const TextStyle(
                            fontSize: 15, fontWeight: FontWeight.w600),
                      ),
                    ),
                    if (method.name == 'Delivery' && deliveryFee != null)
                      Text(
                        '+ ${formats.money(deliveryFee!)}',
                        style: const TextStyle(
                          fontSize: 13,
                          fontWeight: FontWeight.w600,
                          color: KhadraColors.neutral700,
                        ),
                      ),
                  ],
                ),
              ),
            ),
        if (!deliveryOffered)
          Align(
            alignment: AlignmentDirectional.centerStart,
            child: Padding(
              padding: const EdgeInsets.only(top: Space.xs),
              child: Text(
                l10n.galleryDeliveryNotOffered,
                style: const TextStyle(
                    color: KhadraColors.neutral500, fontSize: 12),
              ),
            ),
          ),
      ],
    );
  }
}

/// Where the car should be brought.
///
/// The radius circle is drawn from the gallery's own figure, so a customer can see
/// what is in range before choosing. The SERVER still decides — the quote is
/// refused with `booking.delivery_out_of_range` — because the app's circle is a
/// straight-line approximation and the platform's is the authority.
class _DeliveryPointPicker extends StatefulWidget {
  const _DeliveryPointPicker({
    required this.gallery,
    required this.chosen,
    required this.onChanged,
  });

  final PublicGallery gallery;
  final LatLng? chosen;
  final ValueChanged<LatLng> onChanged;

  @override
  State<_DeliveryPointPicker> createState() => _DeliveryPointPickerState();
}

class _DeliveryPointPickerState extends State<_DeliveryPointPicker> {
  late final MapController _controller = MapController();

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final origin = LatLng(widget.gallery.latitude, widget.gallery.longitude);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(
          l10n.bookDeliveryLocation,
          style: const TextStyle(fontSize: 15, fontWeight: FontWeight.w600),
        ),
        const SizedBox(height: Space.sm),
        ClipRRect(
          borderRadius: Radii.card,
          child: SizedBox(
            height: 240,
            child: FlutterMap(
              mapController: _controller,
              options: MapOptions(
                initialCenter: widget.chosen ?? origin,
                initialZoom: 12,
                onTap: (_, point) => widget.onChanged(point),
              ),
              children: [
                TileLayer(
                  urlTemplate: 'https://tile.openstreetmap.org/{z}/{x}/{y}.png',
                  userAgentPackageName: 'com.khadra.khadra_mobile',
                ),
                CircleLayer(
                  circles: [
                    CircleMarker(
                      point: origin,
                      radius: widget.gallery.delivery.radiusKm.toDouble() * 1000,
                      useRadiusInMeter: true,
                      color: KhadraColors.accent.withValues(alpha: 0.10),
                      borderColor: KhadraColors.accent.withValues(alpha: 0.45),
                      borderStrokeWidth: 1.5,
                    ),
                  ],
                ),
                MarkerLayer(
                  markers: [
                    Marker(
                      point: origin,
                      width: 32,
                      height: 32,
                      child: const Icon(Icons.store_mall_directory,
                          color: KhadraColors.neutral700, size: 26),
                    ),
                    if (widget.chosen != null)
                      Marker(
                        point: widget.chosen!,
                        width: 40,
                        height: 40,
                        child: const Icon(Icons.location_on,
                            color: KhadraColors.accent, size: 36),
                      ),
                  ],
                ),
              ],
            ),
          ),
        ),
        const SizedBox(height: Space.sm),
        Text(
          widget.chosen == null
              ? l10n.bookLocationRequired
              : l10n.bookLocationChosen,
          style: TextStyle(
            fontSize: 13,
            color: widget.chosen == null
                ? KhadraColors.warn
                : KhadraColors.accent,
            fontWeight: FontWeight.w600,
          ),
        ),
      ],
    );
  }
}

/// The price, exactly as the server computed it.
class _PriceBreakdown extends StatelessWidget {
  const _PriceBreakdown({
    required this.quote,
    required this.formats,
    required this.securityDeposit,
  });

  final RentalQuote quote;
  final Formats formats;
  final Money securityDeposit;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final pricing = quote.pricing;

    return KhadraCard(
      child: Column(
        children: [
          KhadraDetailRow(
            label: l10n.bookDailyRate,
            // The DAY COUNT is the server's. Rentals are billed in Amman calendar
            // days, and a phone subtracting two instants gets a different answer
            // for the same rental -- one that would contradict the invoice.
            value: Text(
              '${formats.money(pricing.dailyRate)} × ${l10n.bookDays(pricing.days)}',
            ),
          ),
          KhadraDetailRow(
            label: l10n.bookRentalTotal,
            value: Text(formats.money(pricing.rentalTotal)),
          ),
          if (!pricing.deliveryFee.isZero)
            KhadraDetailRow(
              label: l10n.bookDeliveryFee,
              value: Text(formats.money(pricing.deliveryFee)),
            ),
          const Padding(
            padding: EdgeInsets.symmetric(vertical: Space.sm),
            child: Divider(height: 1),
          ),
          KhadraDetailRow(
            label: l10n.bookTotal,
            value: Text(formats.money(pricing.totalPrice)),
            valueStyle: const TextStyle(
              fontSize: 17,
              fontWeight: FontWeight.w700,
              color: KhadraColors.text,
            ),
          ),
          const SizedBox(height: Space.sm),
          KhadraDetailRow(
            label: l10n.bookDepositNow(formats.percent(pricing.depositPercent)),
            value: Text(formats.money(pricing.depositAmount)),
            valueStyle: const TextStyle(
              fontSize: 15,
              fontWeight: FontWeight.w700,
              color: KhadraColors.accent,
            ),
          ),
          KhadraDetailRow(
            label: l10n.bookBalanceAtPickup,
            value: Text(formats.money(pricing.balanceDue)),
          ),
          KhadraDetailRow(
            label: l10n.vehicleSecurityDeposit,
            value: Text(formats.money(securityDeposit)),
          ),
          const SizedBox(height: Space.sm),
          Align(
            alignment: AlignmentDirectional.centerStart,
            child: Text(
              l10n.bookCalendarDaysNote,
              style: const TextStyle(
                  color: KhadraColors.neutral500, fontSize: 12, height: 1.45),
            ),
          ),
        ],
      ),
    );
  }
}

/// The rules this booking would freeze, in the server's own numbers.
class _Terms extends StatelessWidget {
  const _Terms({required this.quote, required this.formats});

  final RentalQuote quote;
  final Formats formats;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final terms = quote.terms;

    return KhadraCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          _Bullet(l10n.bookTermsPayAfterApproval(
              _hours(terms.paymentWindowHours))),
          _Bullet(l10n.bookTermsPaymentWindow(
              _hours(terms.paymentWindowHours))),
          _Bullet(l10n.bookTermsFreeCancellation(
              _hours(terms.freeCancellationWindowHours))),
          _Bullet(l10n.bookTermsCancellationPenalty(
              formats.percent(terms.customerCancellationPenaltyPercent))),
        ],
      ),
    );
  }

  /// Windows arrive as fractional hours (0.5, 1, 24). Rendering "1.0 hours" reads
  /// like a computed value where a human chose a round number.
  static String _hours(num value) => value == value.roundToDouble()
      ? value.round().toString()
      : value.toString();
}

class _Bullet extends StatelessWidget {
  const _Bullet(this.text);

  final String text;

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.only(bottom: Space.sm),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const Padding(
              padding: EdgeInsetsDirectional.only(top: 5, end: Space.sm),
              child: Icon(Icons.circle, size: 6, color: KhadraColors.accent),
            ),
            Expanded(
              child: Text(
                text,
                style: const TextStyle(fontSize: 14, height: 1.5),
              ),
            ),
          ],
        ),
      );
}

class _SubmitBar extends StatelessWidget {
  const _SubmitBar({
    required this.enabled,
    required this.busy,
    required this.total,
    required this.onSubmit,
  });

  final bool enabled;
  final bool busy;
  final String total;
  final VoidCallback onSubmit;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);

    return Container(
      decoration: const BoxDecoration(
        color: KhadraColors.surface,
        border: Border(top: BorderSide(color: KhadraColors.neutral200)),
      ),
      child: SafeArea(
        child: Padding(
          padding: const EdgeInsets.all(Space.lg),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Row(
                children: [
                  Expanded(
                    child: Text(
                      l10n.bookTotal,
                      style: const TextStyle(
                          color: KhadraColors.neutral600, fontSize: 13),
                    ),
                  ),
                  Text(
                    total,
                    style: const TextStyle(
                        fontSize: 18, fontWeight: FontWeight.w700),
                  ),
                ],
              ),
              const SizedBox(height: Space.md),
              KhadraSubmitButton(
                label: busy ? l10n.bookRequesting : l10n.bookRequest,
                busy: busy,
                onPressed: enabled ? onSubmit : null,
              ),
            ],
          ),
        ),
      ),
    );
  }
}

/// What a customer is told the moment a request lands.
///
/// The window is the BOOKING's, not `/app-config`'s: the booking froze its own
/// terms, and quoting today's setting against a booking made under another would
/// be the exact mistake the freezing exists to prevent.
class _RequestSentDialog extends ConsumerWidget {
  const _RequestSentDialog({required this.booking, required this.galleryName});

  final Booking booking;
  final String galleryName;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);
    final answerHours = booking.decisionDeadline
        .difference(booking.createdAt)
        .inHours
        .toString();

    return AlertDialog(
      icon: const Icon(Icons.check_circle_outline,
          color: KhadraColors.accent, size: 44),
      title: Text(l10n.bookDoneTitle),
      content: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            l10n.bookDoneBody(galleryName, answerHours),
            style: const TextStyle(fontSize: 14, height: 1.5),
          ),
          const SizedBox(height: Space.md),
          // The reference is Latin and is read out on the phone, so it is isolated
          // to keep it upright inside Arabic.
          LatinRun(
            booking.reference,
            style: const TextStyle(
              fontSize: 16,
              fontWeight: FontWeight.w700,
              letterSpacing: 0.5,
            ),
          ),
        ],
      ),
      actions: [
        FilledButton(
          onPressed: () => Navigator.of(context).pop(),
          child: Text(l10n.bookViewBooking),
        ),
      ],
    );
  }
}
