import 'dart:convert';
import 'dart:io';

import 'package:flutter/widgets.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intl/date_symbol_data_local.dart';
import 'package:khadra_mobile/api/dtos.dart';
import 'package:khadra_mobile/core/format/formats.dart';
import 'package:khadra_mobile/features/invoices/document_content.dart';
import 'package:khadra_mobile/features/invoices/invoice_presentation.dart';
import 'package:khadra_mobile/l10n/app_localizations.dart';
import 'package:timezone/data/latest_all.dart' as tz_data;
import 'package:timezone/timezone.dart' as tz;

/// Issued documents in words (payments Phase 5b), against the app's REAL strings
/// and the SHARED contract fixture. Inside a document every word and figure is the
/// stored one; around it, the words are the ones the owner approved (§11 of the
/// plan) — the same the website prints.
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
  String plain(String text) => text.replaceAll('\u2068', '').replaceAll('\u2069', '');

  final fixture = jsonDecode(
    File('${Directory.current.parent.path}/docs/contracts/financial-documents-v1.json').readAsStringSync(),
  ) as Map<String, dynamic>;
  FinancialDocumentPage page(String name) => FinancialDocumentPage.fromJson(jsonDecode(jsonEncode(
        (fixture['documents'] as List<dynamic>).cast<Map<String, dynamic>>().firstWhere((entry) => entry['name'] == name)['page'],
      )) as Map<String, dynamic>);
  List<DocumentLineView> linesOf(DocumentBodyView body) =>
      [for (final section in body.sections) for (final block in section.blocks) if (block is LinesBlock) ...block.lines];

  group('the stored amount and the frozen time', () {
    test('an amount keeps every stored digit, grouped as every amount is, never through a double', () {
      expect(Formats.groupStoredAmount('0.000'), '0.000');
      expect(Formats.groupStoredAmount('94.500'), '94.500');
      expect(Formats.groupStoredAmount('1234567.890'), '1,234,567.890');
      expect(Formats.groupStoredAmount('9007199254740993.125'), '9,007,199,254,740,993.125');
      expect(Formats.groupStoredAmount('-12.500'), '-12.500');
      expect(Formats.groupStoredAmount('-0.000'), '0.000');
      expect(Formats.groupStoredAmount('12,5'), isNull);
      expect(Formats.groupStoredAmount('1e3'), isNull);
    });

    test('an amount is never re-scaled by the scale /app-config publishes today', () {
      // The record froze three decimals; a platform later configured for two, or
      // none, must still print what was issued.
      for (final minorUnits in [0, 2, 3]) {
        final formats = Formats(locale: 'en', currency: CurrencyConfig('JOD', minorUnits), zone: tz.getLocation('Asia/Amman'));
        expect(plain(formats.storedMoney('94.500', 'JOD')), 'JOD 94.500', reason: 'minor units $minorUnits');
        expect(plain(formats.storedMoney('0.125', 'JOD')), 'JOD 0.125', reason: 'minor units $minorUnits');
      }
    });

    test('an amount carries its own currency, placed as the live amounts are, in both languages', () {
      expect(plain(formatsFor('en').storedMoney('94.500', 'JOD')), 'JOD 94.500');
      expect(plain(formatsFor('ar').storedMoney('94.500', 'JOD')), '94.500 JOD');
      expect(plain(formatsFor('en').storedMoney('94,5', 'JOD')), 'JOD 94,5');
    });

    test('a frozen Amman wall time is printed as that wall time, and an impossible one as stored', () {
      final english = formatsFor('en').frozenTime('2026-09-03 15:01');
      expect(english, contains('2026'));
      expect(english, contains('15:01'));
      expect(formatsFor('ar').frozenTime('2026-09-03 15:01'), contains('15:01'));
      expect(plain(formatsFor('en').frozenTime('2026-13-45 99:99')), '2026-13-45 99:99');
      expect(plain(formatsFor('en').frozenTime('2026-02-30 10:00')), '2026-02-30 10:00');
      expect(plain(formatsFor('en').frozenTime('not a time')), 'not a time');
    });
  });

  group('a document, rendered exactly as it was issued', () {
    // Built inside each test: the zone database loads in setUpAll, after declaration.
    InvoicePageView view() => invoicePage(page('payment-receipt-paid-in-full'), en, formatsFor('en'));

    test('shows the stored title and figure, the figure as the stored string', () {
      final body = view().body!;
      expect(body.title, 'Payment receipt');
      expect(body.headlineLabel, 'Amount paid');
      expect(plain(body.headline), 'JOD 94.500');
    });

    test('shows every time as its frozen Amman wall time, never the UTC instant', () {
      final issued = linesOf(view().body!).firstWhere((line) => line.key.endsWith(':issuedAt'));
      expect(issued.value, formatsFor('en').frozenTime('2026-09-03 15:01'));
    });

    test('keeps an unlabelled line as a value of its own, in its place', () {
      final position = view().body!.sections.firstWhere((section) => section.key.endsWith(':bookingPosition'));
      expect(position.blocks.map((block) => block.runtimeType), [LinesBlock, AloneBlock]);
      expect((position.blocks.last as AloneBlock).text, 'Paid in full online. Nothing is due to the rental office.');
    });

    test('keeps a Latin literal left to right, lets a name in Arabic take its own direction, and isolates nothing else', () {
      final lines = linesOf(view().body!);
      expect(lines.firstWhere((line) => line.key.endsWith(':number')).direction, LiteralDirection.ltr);
      expect(lines.firstWhere((line) => line.key.endsWith(':supportPhone')).direction, LiteralDirection.ltr);
      expect(lines.firstWhere((line) => line.key.endsWith(':amountPaid')).direction, isNull);
      expect(directionOf(const PlainValue('أوتو رنت (Auto Rent)')), LiteralDirection.auto);
    });

    test('says what the document says in Arabic, from the same answer', () {
      final arabic = invoicePage(page('payment-receipt-paid-in-full'), ar, formatsFor('ar'));
      expect(arabic.body!.title, 'إيصال دفع');
      expect(arabic.body!.notice, 'هذا المستند ليس فاتورة ضريبية.');
    });

    test('cannot show a schema version it does not know, and keeps the facts the page carries', () {
      final json = page('payment-receipt-paid-in-full');
      final unknown = FinancialDocumentPage(
        row: json.row,
        snapshotSchemaVersion: 2,
        snapshot: json.snapshot,
        links: json.links,
        voided: json.voided,
      );
      final fallback = invoicePage(unknown, en, formatsFor('en'));
      expect(fallback.body, isNull);
      expect(fallback.headlineLabel, 'Amount paid');
      expect(fallback.issued, startsWith('Issued '));
    });
  });

  group('what surrounds a document', () {
    test('links an earlier version to the NEWEST — the highest of the versions, never merely the next', () {
      final superseded = invoicePage(page('booking-statement-superseded'), en, formatsFor('en'));
      expect(superseded.standing?.label, 'Earlier version');
      expect(plain(superseded.newer!.text), 'A newer version exists: TEST-STM-2026-000003');

      // v1 superseded, v2 voided, v3 its correction: v1's next member is the voided v2.
      final statement = page('booking-statement-superseded');
      FinancialDocumentLink link(int version, String status) =>
          FinancialDocumentLink(documentId: 'v$version', type: 'BookingStatement', number: 'STM-$version', version: version, status: status);
      final chain = FinancialDocumentPage(
        row: statement.row,
        snapshotSchemaVersion: 1,
        snapshot: statement.snapshot,
        links: FinancialDocumentLinks(
          versions: [link(1, 'Superseded'), link(3, 'Current'), link(2, 'Voided')],
          previousVersion: null,
          nextVersion: link(2, 'Voided'),
          replacedBy: null,
          paymentReceipt: null,
          refundReceipts: const [],
        ),
        voided: null,
      );
      final newer = invoicePage(chain, en, formatsFor('en')).newer!;
      expect(newer.id, 'v3');
      expect(newer.number, 'STM-3');
    });

    test('says a voided document was voided and what replaced it — never why — in the approved words', () {
      final english = invoicePage(page('payment-receipt-deposit-voided'), en, formatsFor('en')).voided!;
      expect(english.text, startsWith('Voided on '));
      expect(plain(english.replacement!.text), 'Replaced by TEST-PAY-2026-000005.');
      final arabic = invoicePage(page('payment-receipt-deposit-voided'), ar, formatsFor('ar')).voided!;
      expect(plain('${arabic.text} ${arabic.replacement!.text}'), startsWith('أُلغي في '));
      expect(plain('${arabic.text} ${arabic.replacement!.text}'), endsWith('، وحلّ محلّه TEST-PAY-2026-000005.'));
    });

    test('says only when a void has no replacement to name', () {
      final voided = page('payment-receipt-deposit-voided');
      final alone = FinancialDocumentPage(
        row: voided.row,
        snapshotSchemaVersion: 1,
        snapshot: voided.snapshot,
        links: voided.links,
        voided: FinancialDocumentVoidNotice(voidedAt: voided.voided!.voidedAt, replacedBy: null),
      );
      final view = invoicePage(alone, en, formatsFor('en')).voided!;
      expect(view.replacement, isNull);
      expect(view.text, endsWith('.'));
    });

    test('a void that arrives without its date still says voided — never "Voided on ."', () {
      final voided = page('payment-receipt-deposit-voided');
      FinancialDocumentPage undated(FinancialDocumentLink? replacedBy) => FinancialDocumentPage(
            row: voided.row,
            snapshotSchemaVersion: 1,
            snapshot: voided.snapshot,
            links: voided.links,
            voided: FinancialDocumentVoidNotice(voidedAt: null, replacedBy: replacedBy),
          );

      final english = invoicePage(undated(voided.voided!.replacedBy), en, formatsFor('en')).voided!;
      expect(english.text, 'Voided');
      expect(plain(english.replacement!.text), 'Replaced by TEST-PAY-2026-000005.');
      expect(invoicePage(undated(null), ar, formatsFor('ar')).voided!.text, 'ملغى');
    });

    test('lists every version, marking the one on screen and the current one', () {
      final view = invoicePage(page('payment-receipt-deposit-correction'), en, formatsFor('en'));
      expect(view.versionOf, 'Version 2 of 2');
      expect([for (final link in view.versions) (link.number, link.here, link.standing?.label)], [
        ('TEST-PAY-2026-000001', false, 'Voided'),
        ('TEST-PAY-2026-000005', true, 'Current version'),
      ]);
    });

    test('parts a link sentence around its number, so a screen can keep the number whole', () {
      final refund = invoicePage(page('refund-receipt-free-cancellation'), en, formatsFor('en')).paymentReceipt!;
      expect(refund.aroundNumber, (before: 'Issued against payment receipt', after: ''));
      expect(
        invoicePage(page('payment-receipt-deposit-voided'), en, formatsFor('en')).voided!.replacement!.aroundNumber,
        (before: 'Replaced by', after: '.'),
      );
      expect(
        invoicePage(page('payment-receipt-deposit-voided'), ar, formatsFor('ar')).voided!.replacement!.aroundNumber,
        (before: 'وحلّ محلّه', after: '.'),
      );
      expect(
        invoicePage(page('booking-statement-superseded'), en, formatsFor('en')).newer!.aroundNumber,
        (before: 'A newer version exists:', after: ''),
      );
      expect(const LinkText('x', 'A sentence without it', 'TEST-PAY-2026-000001').aroundNumber, isNull);
    });

    test('links a refund receipt to its payment receipt, and a payment receipt to its refunds', () {
      expect(
        plain(invoicePage(page('refund-receipt-free-cancellation'), en, formatsFor('en')).paymentReceipt!.text),
        'Issued against payment receipt TEST-PAY-2026-000002',
      );
      expect(
        invoicePage(page('payment-receipt-paid-in-full'), en, formatsFor('en')).refundReceipts.map((link) => link.number),
        ['TEST-RFD-2026-000001'],
      );
    });

    test('spells out a standing it does not know rather than presenting the document as current', () {
      expect(standingOf('Current', en), isNull);
      expect(standingOf('Voided', ar)?.label, 'ملغى');
      final unknown = standingOf('Withdrawn', en)!;
      expect(unknown.label, 'Withdrawn');
      expect(unknown.unknown, isTrue);
    });
  });

  group('lists', () {
    final rows = Paged.fromJson(fixture['myDocuments'] as Map<String, dynamic>, FinancialDocumentRow.fromJson).items;

    test('a row shows its stored title and figure, the figure through the LIVE formatter', () {
      final correction = invoiceRow(rows.firstWhere((row) => row.number == 'TEST-PAY-2026-000005'), en, formatsFor('en'));
      expect(correction.title, 'Payment receipt');
      expect(correction.version, 'Version 2');
      expect(correction.standing, isNull);
      expect(plain(correction.headline), 'JOD 19.500');
      expect(plain(correction.booking), startsWith('Booking KH-'));
      expect(invoiceRow(rows.firstWhere((row) => row.number == 'TEST-PAY-2026-000001'), en, formatsFor('en')).standing?.label, 'Voided');
      expect(invoiceRow(rows.firstWhere((row) => row.number == 'TEST-PAY-2026-000002'), en, formatsFor('en')).version, isNull);
    });

    test('what is being prepared is worded by kind, dated, and a kind this build does not know plainly', () {
      PendingFinancialDocument pending(String type) =>
          PendingFinancialDocument(type: type, subjectId: 's-1', occurredAt: DateTime.utc(2026, 9, 3, 12, 32));
      expect(preparingRow(pending('PaymentReceipt'), en, formatsFor('en')).label, 'Payment receipt — being prepared');
      expect(preparingRow(pending('RefundReceipt'), ar, formatsFor('ar')).label, 'إيصال استرداد — قيد الإعداد');
      expect(preparingRow(pending('BookingStatement'), ar, formatsFor('ar')).label, 'كشف حساب الحجز — قيد الإعداد');
      expect(preparingRow(pending('PayablesStatement'), en, formatsFor('en')).label, 'A document — being prepared');
      expect(preparingRow(pending('PaymentReceipt'), en, formatsFor('en')).date, contains('2026'));
    });
  });
}
