import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intl/date_symbol_data_local.dart';
import 'package:khadra_mobile/api/dtos.dart';
import 'package:khadra_mobile/core/format/formats.dart';
import 'package:khadra_mobile/core/providers.dart';
import 'package:khadra_mobile/core/theme/khadra_theme.dart';
import 'package:khadra_mobile/features/disputes/dispute_screen.dart';
import 'package:khadra_mobile/features/disputes/earlier_decisions.dart';
import 'package:khadra_mobile/l10n/app_localizations.dart';
import 'package:timezone/data/latest_all.dart' as tz_data;
import 'package:timezone/timezone.dart' as tz;

import 'support/fake_api.dart';

/// A later dispute on a booking splits only what earlier ones left (owner,
/// 2026-09-26; pre-launch item 169). The server states every figure — what the
/// booking held, what earlier disputes decided, what this one can split — and
/// the app shows them, never subtracting. Both new fields are ADDITIVE: an
/// older API sends neither, and the app reads that as "nothing to say".
void main() {
  setUpAll(() async {
    tz_data.initializeTimeZones();
    await initializeDateFormatting('en');
    await initializeDateFormatting('ar');
  });

  final en = lookupAppLocalizations(const Locale('en'));
  final ar = lookupAppLocalizations(const Locale('ar'));
  Formats formatsFor(String locale) => Formats(
        locale: locale,
        currency: CurrencyConfig('JOD', 3),
        zone: tz.getLocation('Asia/Amman'),
      );

  /// The isolate marks a price is wrapped in, stripped so a test states the words read.
  String plain(String text) => text.replaceAll('\u2068', '').replaceAll('\u2069', '');
  Map<String, dynamic> jod(num amount) => {'amount': amount, 'currency': 'JOD'};

  /// A dispute as `/api/v1/disputes/{id}` sends one.
  Map<String, dynamic> disputeJson({
    bool live = true,
    num held = 18,
    num? onBooking,
    num? decided,
    Map<String, dynamic>? resolution,
    Map<String, dynamic>? booking,
  }) =>
      {
        'ticketId': 't-2',
        'bookingId': 'b-1',
        'status': live ? 'Open' : 'Resolved',
        'isLive': live,
        'openedByParty': 'Customer',
        'openedByUserId': 'u-1',
        'openedByName': 'Layla Odeh',
        'openedByAccountClosed': false,
        'reason': 'Something else went wrong.',
        'openedAt': '2026-09-26T10:00:00Z',
        'slaDeadline': '2026-09-28T10:00:00Z',
        'isOverdue': false,
        'assignedAdminId': null,
        'assignedAdminName': null,
        'assignedAdminAccountClosed': false,
        'closedAt': live ? null : '2026-09-26T12:00:00Z',
        'statements': [
          {
            'statementId': 's-1',
            'party': 'Customer',
            'authorUserId': 'u-1',
            'authorName': 'Layla Odeh',
            'authorAccountClosed': false,
            'body': 'Something else went wrong.',
            'createdAt': '2026-09-26T10:00:00Z',
            'evidence': const <dynamic>[],
          },
        ],
        'resolution': resolution,
        'depositHeld': jod(held),
        'booking': booking,
        if (onBooking != null) 'depositOnBooking': jod(onBooking),
        if (decided != null) 'decidedByEarlierTickets': jod(decided),
      };

  group('the two figures', () {
    test('are read when the server sends them', () {
      final dispute = Dispute.fromJson(disputeJson(held: 13, onBooking: 18, decided: 5));

      expect(dispute.depositHeld.amount, 13);
      expect(dispute.depositOnBooking!.amount, 18);
      expect(dispute.decidedByEarlierTickets!.amount, 5);
      expect(dispute.decidedByEarlierTickets!.currencyCode, 'JOD');
    });

    test('are absent from an API that predates them, and nothing else changes', () {
      final older = Dispute.fromJson(disputeJson());
      final newer = Dispute.fromJson(disputeJson(onBooking: 18, decided: 0));

      expect(older.depositOnBooking, isNull);
      expect(older.decidedByEarlierTickets, isNull);
      expect(decidedEarlier(older), isNull);
      expect(earlierDecisionNotice(older, en, formatsFor('en')), isNull);
      // Every field the app read before reads the same beside the new ones.
      for (final dispute in [older, newer]) {
        expect(dispute.ticketId, 't-2');
        expect(dispute.status, 'Open');
        expect(dispute.isLive, isTrue);
        expect(dispute.depositHeld.amount, 18);
        expect(dispute.statements, hasLength(1));
      }
    });
  });

  group('the notice on a live ticket', () {
    test('names what is left after a partial decision, in both languages', () {
      final dispute = Dispute.fromJson(disputeJson(held: 13, onBooking: 18, decided: 5));

      expect(
        plain(earlierDecisionNotice(dispute, en, formatsFor('en'))!),
        'An earlier dispute on this booking already decided JOD 5.000 of its JOD 18.000 deposit, '
        'so this one can decide only what is left: JOD 13.000.',
      );
      expect(
        plain(earlierDecisionNotice(dispute, ar, formatsFor('ar'))!),
        'قرّر نزاع سابق على هذا الحجز مصير 5.000 JOD من عربونه البالغ 18.000 JOD، '
        'فلا يملك هذا النزاع إلا المتبقي منه: 13.000 JOD.',
      );
    });

    test('says nothing is left once the whole deposit was decided, in both languages', () {
      final dispute = Dispute.fromJson(disputeJson(held: 0, onBooking: 18, decided: 18));

      expect(
        plain(earlierDecisionNotice(dispute, en, formatsFor('en'))!),
        'An earlier dispute on this booking already decided its whole JOD 18.000 deposit, '
        'so this one has nothing left to split.',
      );
      expect(
        plain(earlierDecisionNotice(dispute, ar, formatsFor('ar'))!),
        'قرّر نزاع سابق على هذا الحجز مصير عربونه كاملًا البالغ 18.000 JOD، '
        'فلم يبقَ منه ما يوزّعه هذا النزاع.',
      );
    });

    test('is not shown on a first dispute, nor once the ticket is closed', () {
      expect(earlierDecisionNotice(Dispute.fromJson(disputeJson(onBooking: 18, decided: 0)), en, formatsFor('en')), isNull);
      expect(
        earlierDecisionNotice(Dispute.fromJson(disputeJson(live: false, held: 0, onBooking: 18, decided: 18)), en, formatsFor('en')),
        isNull,
      );
    });
  });

  group('the screen', () {
    Future<FakeApi> pump(WidgetTester tester, Dispute dispute, Locale locale) async {
      tester.view.physicalSize = const Size(412, 915);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.reset);

      final api = FakeApi()..disputeById = dispute;
      final container = ProviderContainer(overrides: [
        apiProvider.overrideWithValue(api),
        sessionStoreProvider.overrideWithValue(FakeSessionStore()),
        sharedPreferencesProvider.overrideWithValue(null),
        isArabicProvider.overrideWithValue(locale.languageCode == 'ar'),
      ]);
      addTearDown(container.dispose);
      await container.read(sessionProvider.notifier).adoptTokens(FakeApi.fakeTokens());
      await container.read(appConfigProvider.future);

      await tester.pumpWidget(
        UncontrolledProviderScope(
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
            home: const DisputeScreen(ticketId: 't-2'),
          ),
        ),
      );
      await tester.pumpAndSettle();
      return api;
    }

    /// Ends by tearing the screen down, so Riverpod's disposal timer runs inside the test.
    void screenTest(String name, Future<void> Function(WidgetTester) body) {
      testWidgets(name, (tester) async {
        await body(tester);
        await tester.pumpWidget(const SizedBox());
        await tester.pump();
      });
    }

    for (final (locale, l10n) in [(const Locale('en'), en), (const Locale('ar'), ar)]) {
      final tag = locale.languageCode;

      // Owner, 2026-09-30 (payments Phase 8): a withdrawn dispute leaves the booking to its own rules, which may keep a
      // customer's penalty of the whole deposit — so neither message may promise that nothing is charged.
      screenTest('withdrawing says the booking settles by its own rules, before and after, in $tag', (tester) async {
        final api = await pump(tester, Dispute.fromJson(disputeJson()), locale);

        final withdraw = find.widgetWithText(OutlinedButton, l10n.disputeWithdraw);
        await tester.ensureVisible(withdraw);
        await tester.tap(withdraw);
        await tester.pumpAndSettle();

        expect(
          find.text(tag == 'en'
              ? 'The dispute will be withdrawn, and the booking will settle according to its existing cancellation and penalty rules.'
              : 'سيتم سحب النزاع، وسيُسوّى الحجز وفق قواعد الإلغاء والغرامات المطبقة عليه.'),
          findsOneWidget,
        );

        await tester.tap(find.widgetWithText(FilledButton, l10n.disputeWithdraw));
        await tester.pumpAndSettle();

        expect(api.withdrawnTicketIds, ['t-2']);
        expect(
          find.text(tag == 'en'
              ? 'The dispute has been withdrawn, and the booking will settle according to its existing cancellation and penalty rules.'
              : 'تم سحب النزاع، وسيُسوّى الحجز وفق قواعد الإلغاء والغرامات المطبقة عليه.'),
          findsOneWidget,
        );
        expect(find.textContaining(tag == 'en' ? 'Nothing has been charged' : 'لم يُخصم شيء'), findsNothing);
        // The message goes, as a message does, before the screen is torn down.
        await tester.pump(const Duration(seconds: 5));
        await tester.pumpAndSettle();
      });

      screenTest('a second dispute after a whole split says there is nothing left, in $tag', (tester) async {
        await pump(tester, Dispute.fromJson(disputeJson(held: 0, onBooking: 18, decided: 18)), locale);

        expect(tester.takeException(), isNull);
        expect(
          find.textContaining(tag == 'en' ? 'nothing left to split' : 'فلم يبقَ منه ما يوزّعه هذا النزاع'),
          findsOneWidget,
        );
      });

      screenTest('a second dispute after a partial split names the remainder, in $tag', (tester) async {
        await pump(tester, Dispute.fromJson(disputeJson(held: 13, onBooking: 18, decided: 5)), locale);

        expect(tester.takeException(), isNull);
        expect(
          find.textContaining(tag == 'en' ? 'only what is left' : 'إلا المتبقي منه'),
          findsOneWidget,
        );
      });

      screenTest('a resolved later dispute labels its basis as the deposit, in $tag', (tester) async {
        await pump(
          tester,
          Dispute.fromJson(disputeJson(
            live: false,
            held: 0,
            onBooking: 18,
            decided: 18,
            resolution: {
              'depositHeld': jod(0),
              'refundToCustomer': jod(0),
              'retainedByPlatform': jod(0),
              'transferredToDealer': jod(0),
              'dealerCharge': null,
              'waivesEverything': true,
              'note': 'The deposit was already decided by the first dispute.',
              'resolvedByAdminId': 'a-1',
              'resolvedByName': 'Rania Haddad',
              'resolvedByAccountClosed': false,
              'resolvedAt': '2026-09-26T12:00:00Z',
            },
          )),
          locale,
        );

        expect(tester.takeException(), isNull);
        // The booking's deposit, not the car's security deposit the office holds.
        expect(find.text(l10n.disputeDepositHeld), findsOneWidget);
        expect(find.text(l10n.vehicleSecurityDeposit), findsNothing);
        expect(find.text(l10n.disputeDecidedEarlier), findsOneWidget);
        // A closed ticket is deciding nothing now: no notice.
        expect(find.textContaining(tag == 'en' ? 'nothing left to split' : 'فلم يبقَ منه'), findsNothing);
        // Pre-launch item 173: a resolution moves money, so its card never says nothing was charged.
        expect(find.text(l10n.bookingPenaltyNotCharged), findsNothing);
        // Pre-launch item 218: the customer's share is labelled as the customer — never «عليك», "against you", over
        // money that goes back to them.
        expect(find.text(l10n.bookingPartyCustomer), findsWidgets);
        expect(find.text('عليك'), findsNothing);
      });

      // Owner decision 3; pre-launch item 151 (Wave 7). From 1.4.0 the server sends a customer the basis and their
      // own share only; the office's and the platform's shares, the charge to the office and the waiver flag come
      // null. This build does not read them, so a share it was not sent never surfaces as a "0.000" row.
      screenTest('a settled dispute shows the customer their own share and nobody else\'s, in $tag', (tester) async {
        await pump(
          tester,
          Dispute.fromJson(disputeJson(
            live: false,
            held: 18,
            resolution: {
              'depositHeld': jod(18),
              'refundToCustomer': jod(5),
              'retainedByPlatform': null,
              'transferredToDealer': null,
              'dealerCharge': null,
              'waivesEverything': null,
              'note': 'Split after reading both sides.',
              'resolvedByAdminId': null,
              'resolvedByName': 'Khadra',
              'resolvedByAccountClosed': false,
              'resolvedAt': '2026-09-26T12:00:00Z',
            },
          )),
          locale,
        );

        expect(tester.takeException(), isNull);
        expect(find.text(l10n.bookingPartyCustomer), findsWidgets);
        expect(find.textContaining('5.000'), findsOneWidget);
        expect(find.text(l10n.bookingPartyDealer), findsNothing);
        expect(find.text(l10n.bookingPartyAdmin), findsNothing);
        expect(find.textContaining('13.000'), findsNothing, reason: "the office's share is not the customer's");
        expect(find.textContaining(RegExp(r'(^|\D)0\.000')), findsNothing, reason: 'no share stands in as zero');
        expect(find.text('Split after reading both sides.'), findsOneWidget);
      });

      // E2E F43: the decision's refund, as the booking lists it for this ticket, says what became of it.
      screenTest('a settled dispute says what became of its refund, in $tag', (tester) async {
        await pump(
          tester,
          Dispute.fromJson(disputeJson(
            live: false,
            resolution: {
              'depositHeld': jod(18),
              'refundToCustomer': jod(9),
              'note': '',
              'resolvedByName': 'Khadra',
              'resolvedAt': '2026-09-26T12:00:00Z',
            },
            booking: {
              'bookingId': 'b-1',
              'reference': 'KH-24-0007',
              'status': 'Completed',
              'refunds': [
                {
                  'refundId': 'r-0',
                  'reason': 'FreeCancellation',
                  'amount': jod(4),
                  'status': 'Settled',
                  'requestedAt': '2026-09-20T10:00:00Z',
                  'settledAt': '2026-09-21T10:00:00Z',
                },
                {
                  'refundId': 'r-1',
                  'reason': 'DisputeResolution',
                  'amount': jod(9),
                  'status': 'Failed',
                  'requestedAt': '2026-09-26T12:00:00Z',
                  'failedAt': '2026-09-26T13:00:00Z',
                  'disputeTicketId': 't-2',
                },
              ],
            },
          )),
          locale,
        );

        expect(tester.takeException(), isNull);
        final refund = tester.widget<Text>(find.byKey(const ValueKey('dispute-refund'))).data!;
        // The words around the figure, whatever order this harness's formats put it in.
        final [before, after] = l10n.disputeRefundDelayed('{amount}').split('{amount}');
        expect(plain(refund), allOf(startsWith(before), endsWith(after), contains('9.000')));
      });
    }
  });

  test('a resolution the server sent with the other shares withheld reads without them', () {
    final dispute = Dispute.fromJson(disputeJson(
      live: false,
      resolution: {
        'depositHeld': jod(18),
        'refundToCustomer': jod(5),
        'retainedByPlatform': null,
        'transferredToDealer': null,
        'dealerCharge': null,
        'waivesEverything': null,
        'note': '',
        'resolvedAt': '2026-09-26T12:00:00Z',
      },
    ));

    expect(dispute.resolution!.depositHeld.amount, 18);
    expect(dispute.resolution!.refundToCustomer.amount, 5);
  });
}
