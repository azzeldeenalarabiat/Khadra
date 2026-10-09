import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intl/date_symbol_data_local.dart';
import 'package:khadra_mobile/api/dtos.dart';
import 'package:khadra_mobile/core/api/api_failure.dart';
import 'package:khadra_mobile/core/api/api_failure_messages.dart';
import 'package:khadra_mobile/core/format/formats.dart';
import 'package:khadra_mobile/core/providers.dart';
import 'package:khadra_mobile/core/theme/khadra_theme.dart';
import 'package:khadra_mobile/features/bookings/request_booking_screen.dart';
import 'package:khadra_mobile/features/catalogue/vehicle_row.dart';
import 'package:khadra_mobile/features/disputes/dispute_screen.dart';
import 'package:khadra_mobile/features/notifications/notifications_screen.dart';
import 'package:khadra_mobile/l10n/app_localizations.dart';
import 'package:timezone/data/latest_all.dart' as tz_data;
import 'package:timezone/timezone.dart' as tz;

import 'support/fake_api.dart';

/// Wave 7: the app says what the website already says, from facts the API already
/// serves (pre-launch item 103; E2E W4-9, F1, F2, F10, F43, D10). Every sentence is the
/// website's, in both languages; none is a figure the app worked out.
void main() {
  setUpAll(() async {
    tz_data.initializeTimeZones();
    await initializeDateFormatting('en');
    await initializeDateFormatting('ar');
  });

  final en = lookupAppLocalizations(const Locale('en'));
  final ar = lookupAppLocalizations(const Locale('ar'));
  Formats formatsFor(String locale) =>
      Formats(locale: locale, currency: CurrencyConfig('JOD', 3), zone: tz.getLocation('Asia/Amman'));
  String plain(String text) =>
      text.replaceAll(String.fromCharCode(0x2068), '').replaceAll(String.fromCharCode(0x2069), '');

  // ── W4-9: a request refused over a document Khadra could not accept ─────────────────────

  group('W4-9: the documents a refusal names', () {
    ApiFailure refused(Object? rejected) => ApiFailure(
          kind: ApiFailureKind.forbidden,
          code: 'booking.documents_incomplete',
          statusCode: 403,
          extensions: {'missingDocumentTypes': const <String>[], 'rejectedDocumentTypes': ?rejected},
        );

    test('are named, in the reader\'s language, as the website names them', () {
      final failure = refused(['DrivingLicenceFront', 'NationalId']);

      expect(
        rejectedDocumentsMessage(en, failure),
        'Khadra could not accept your Driving licence — front, National ID. '
        'Upload a new one from your documents, then send the request again.',
      );
      expect(
        rejectedDocumentsMessage(ar, failure),
        'لم تتمكن خضرا من قبول رخصة القيادة — الوجه الأمامي، الهوية الوطنية. '
        'ارفع نسخة جديدة من صفحة مستنداتك، ثم أرسل الطلب مرة أخرى.',
      );
    });

    test('leave the general sentence when none is named: an older API, or documents only missing', () {
      expect(rejectedDocumentsMessage(en, refused(null)), isNull);
      expect(rejectedDocumentsMessage(en, refused(const <String>[])), isNull);
      expect(rejectedDocumentsMessage(en, refused('DrivingLicenceFront')), isNull);
      expect(refused(null).messageFor(en), en.errorBookingDocumentsIncomplete);
    });

    test('belong to that refusal only', () {
      const other = ApiFailure(
        kind: ApiFailureKind.conflict,
        code: 'booking.vehicle_unavailable',
        extensions: {'rejectedDocumentTypes': ['NationalId']},
      );
      expect(rejectedDocumentsMessage(en, other), isNull);
    });
  });

  // ── F1: a refused price, in its own reason ─────────────────────────────────────────────

  group('F1: a refused price', () {
    ApiFailure coded(String code) => ApiFailure(kind: ApiFailureKind.validation, code: code, statusCode: 400);

    test('says which handover is outside the office\'s hours, in both languages', () {
      expect(coded('booking.pickup_outside_opening_hours').messageFor(en),
          "The pickup time is outside the office's opening hours.");
      expect(coded('booking.return_outside_opening_hours').messageFor(en),
          "The return time is outside the office's opening hours.");
      expect(coded('booking.pickup_outside_opening_hours').messageFor(ar), 'وقت الاستلام خارج ساعات عمل المكتب.');
      expect(coded('booking.return_outside_opening_hours').messageFor(ar), 'وقت الإرجاع خارج ساعات عمل المكتب.');
    });

    test('words a return before the pickup instead of the server\'s English', () {
      expect(coded('period.end_before_start').messageFor(en), en.validationReturnAfterPickup);
      expect(coded('period.end_before_start').messageFor(ar), ar.validationReturnAfterPickup);
    });

    test('offers delivery only for a counter pickup out of hours, where the office and the car can deliver', () {
      bool offers(String code, {bool self = true, bool delivers = true, bool eligible = true}) =>
          offersDeliveryInstead(coded(code), selfPickup: self, galleryDelivers: delivers, carIsDeliveryEligible: eligible);

      expect(offers('booking.pickup_outside_opening_hours'), isTrue);
      expect(offers('booking.return_outside_opening_hours'), isTrue);
      expect(offers('booking.pickup_outside_opening_hours', self: false), isFalse, reason: 'already a delivery');
      expect(offers('booking.pickup_outside_opening_hours', delivers: false), isFalse);
      expect(offers('booking.pickup_outside_opening_hours', eligible: false), isFalse);
      expect(offers('booking.too_soon'), isFalse);
      expect(en.quoteDeliveryInstead,
          'This office can deliver the car at these times instead: choose delivery when you send the request.');
    });
  });

  // ── F10: the bearer challenge's two codes ──────────────────────────────────────────────

  test('F10: a refused session reads in the reader\'s language, never the server\'s English title', () {
    ApiFailure challenged(String code) => ApiFailure(
          kind: ApiFailureKind.unauthorized,
          code: code,
          statusCode: 401,
          title: 'Your session is no longer valid. Sign in again.',
        );

    expect(challenged('auth.session_invalid').messageFor(ar), ar.authSessionExpired);
    expect(challenged('auth.unauthenticated').messageFor(ar), ar.authSignInToContinue);
    expect(challenged('auth.session_invalid').messageFor(en), isNot(contains('no longer valid')));
  });

  // ── F43: what became of a dispute's refund ─────────────────────────────────────────────

  group('F43: what became of the refund a dispute decided', () {
    Refund refund(String status, {String? ticket = 't-2'}) => Refund.maybe({
          'refundId': 'r-1',
          'paymentId': 'p-1',
          'reason': 'DisputeResolution',
          'amount': {'amount': 9, 'currency': 'JOD'},
          'status': status,
          'requestedAt': '2026-10-06T09:00:00Z',
          'sentAt': status == 'Requested' ? null : '2026-10-06T10:00:00Z',
          'settledAt': status == 'Settled' ? '2026-10-07T08:30:00Z' : null,
          'failedAt': status == 'Failed' ? '2026-10-06T11:00:00Z' : null,
          'disputeTicketId': ticket,
        })!;
    String? said(String status, {AppLocalizations? l10n, String locale = 'en', String? ticket = 't-2'}) {
      final text = disputeRefundText(l10n ?? en, formatsFor(locale), 't-2', [refund(status, ticket: ticket)]);
      return text == null ? null : plain(text);
    }

    test('reads the refund for THIS ticket, with when it was sent and when it arrived', () {
      expect(refund('Sent').disputeTicketId, 't-2');
      expect(refund('Sent').sentAt, DateTime.utc(2026, 10, 6, 10));
      expect(said('Requested'),
          'Your refund of JOD 9.000 has been requested and will go to your original payment method.');
      expect(said('Sent'), allOf(contains('Your refund of JOD 9.000 is on its way'), contains('1:00')));
      expect(said('Settled'), allOf(startsWith('JOD 9.000 was refunded to your original payment method on'), contains('11:30')));
      expect(said('Failed'),
          'Your refund of JOD 9.000 has not gone through yet. Khadra is sending it again; there is nothing you need to do.');
    });

    test('in Arabic too', () {
      expect(said('Requested', l10n: ar, locale: 'ar'), 'طُلب استرداد 9.000 JOD لك، وسيُرسل إلى وسيلة الدفع الأصلية.');
      expect(said('Failed', l10n: ar, locale: 'ar'), 'لم يكتمل استرداد 9.000 JOD بعد، وتعيد خضرا إرساله. لا يلزمك فعل شيء.');
    });

    test('says nothing for another ticket\'s refund, any other refund, or a status it does not know', () {
      expect(said('Settled', ticket: 't-1'), isNull);
      expect(said('Settled', ticket: null), isNull);
      expect(said('Reversed'), isNull);
      expect(disputeRefundText(en, formatsFor('en'), 't-2', null), isNull);
    });
  });

  // ── The new fields, and what an older API leaves out ───────────────────────────────────

  group('the fields the API already serves', () {
    test('are read, and absent from an older API without changing anything else', () {
      expect(BookingDispute.listOf([
        {'ticketId': 't-1', 'status': 'Withdrawn', 'openedAt': '2026-10-06T09:00:00Z', 'closedAt': '2026-10-06T10:00:00Z'},
        {'status': 'Open'},
      ]).single.isWithdrawn, isTrue);
      expect(BookingDispute.listOf(null), isEmpty);
      expect(Booking.fromJson(const {'bookingId': 'b-1', 'status': 'Completed'}).disputes, isEmpty);

      expect(PenaltyAssessment.maybe({'attributedTo': 'Dealer', 'reasonCode': 'DealerDidNotHandOver'})!.reasonCode,
          'DealerDidNotHandOver');
      expect(PenaltyAssessment.maybe({'attributedTo': 'Dealer'})!.reasonCode, isNull);

      Map<String, dynamic> listing([bool? selfPickup]) => {
            'vehicleId': 'v-1',
            'make': 'Kia',
            'model': 'Picanto',
            'dailyRate': {'amount': 20, 'currency': 'JOD'},
            'gallery': {'dealerId': 'd-1', 'businessName': 'Petra Rentals'},
            'selfPickupAvailable': ?selfPickup,
          };
      expect(CatalogueListing.fromJson(listing(false)).selfPickupAvailable, isFalse);
      expect(CatalogueListing.fromJson(listing()).selfPickupAvailable, isNull);

      expect(NotificationItem.fromJson(const {'kind': 'YourBookingApproved', 'actorStandIn': 'RentalOffice', 'occurredAt': '2026-10-06T09:00:00Z'}).actorStandIn,
          'RentalOffice');
    });
  });

  // ── Screens ────────────────────────────────────────────────────────────────────────────

  Future<void> pump(WidgetTester tester, FakeApi api, Widget child, {Locale locale = const Locale('en')}) async {
    tester.view.physicalSize = const Size(412, 915);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);
    final container = ProviderContainer(overrides: [
      apiProvider.overrideWithValue(api),
      sessionStoreProvider.overrideWithValue(FakeSessionStore()),
      sharedPreferencesProvider.overrideWithValue(null),
      isArabicProvider.overrideWithValue(locale.languageCode == 'ar'),
    ]);
    addTearDown(container.dispose);
    await container.read(sessionProvider.notifier).adoptTokens(FakeApi.fakeTokens());
    await container.read(appConfigProvider.future);
    await tester.pumpWidget(UncontrolledProviderScope(
      container: container,
      child: MaterialApp(
        locale: locale,
        theme: KhadraTheme.light(),
        supportedLocales: AppLocalizations.supportedLocales,
        localizationsDelegates: const [
          AppLocalizations.delegate,
          GlobalMaterialLocalizations.delegate,
          GlobalWidgetsLocalizations.delegate,
          GlobalCupertinoLocalizations.delegate,
        ],
        home: child,
      ),
    ));
    await tester.pumpAndSettle();
  }

  /// Tears the screen down inside the test, so Riverpod's disposal timer runs here.
  void screenTest(String name, Future<void> Function(WidgetTester) body) {
    testWidgets(name, (tester) async {
      await body(tester);
      await tester.pumpWidget(const SizedBox());
      await tester.pump();
    });
  }

  NotificationItem alert(String kind, {String actor = 'Petra Rentals', String? standIn}) => NotificationItem(
        notificationId: 'n-$kind',
        kind: kind,
        subjectId: 'b-1',
        subjectReference: 'KH-24-0007',
        actorName: actor,
        occurredAt: DateTime.utc(2026, 10, 6, 9),
        readAt: null,
        actorStandIn: standIn,
      );

  screenTest('103: an office no longer on the platform is "the rental office" in the reader\'s language', (tester) async {
    final api = FakeApi()
      ..notificationFeed = NotificationFeed(
        items: [alert('YourBookingApproved', actor: 'The rental office', standIn: 'RentalOffice')],
        page: 1,
        pageSize: 25,
        totalCount: 1,
        unreadCount: 1,
      );
    await pump(tester, api, const NotificationsScreen(), locale: const Locale('ar'));

    expect(find.text(ar.notificationYourBookingApproved(ar.notificationActorRentalOffice)), findsOneWidget);
    expect(find.textContaining('The rental office'), findsNothing);
  });

  screenTest("D10: an opened dispute reads as one, not the generic line", (tester) async {
    final api = FakeApi()
      ..notificationFeed = NotificationFeed(
        items: [alert('YourDisputeOpened', actor: 'Khadra')],
        page: 1,
        pageSize: 25,
        totalCount: 1,
        unreadCount: 1,
      );
    await pump(tester, api, const NotificationsScreen());

    expect(find.text('Your dispute is open, and Khadra will decide it'), findsOneWidget);
    expect(find.text(en.notificationUnknown('Khadra')), findsNothing);
  });

  for (final (selfPickup, shown) in [(false, true), (true, false), (null, false)]) {
    screenTest('F2: a result ${shown ? 'is' : 'is not'} marked "Delivery only at these times" ($selfPickup)', (tester) async {
      final listing = CatalogueListing.fromJson({
        'vehicleId': 'v-1',
        'make': 'Kia',
        'model': 'Picanto',
        'year': 2024,
        'transmission': 'Automatic',
        'fuelType': 'Petrol',
        'seats': 4,
        'dailyRate': {'amount': 20, 'currency': 'JOD'},
        'isDeliveryAvailable': true,
        'gallery': {'dealerId': 'd-1', 'businessName': 'Petra Rentals'},
        'selfPickupAvailable': ?selfPickup,
      });
      await pump(tester, FakeApi(), Scaffold(body: VehicleRow(listing: listing)));

      expect(find.byKey(const ValueKey('delivery-only')), shown ? findsOneWidget : findsNothing);
      if (shown) expect(find.text(en.vehicleDeliveryOnlyThen), findsOneWidget);
      expect(tester.takeException(), isNull);
    });
  }
}
