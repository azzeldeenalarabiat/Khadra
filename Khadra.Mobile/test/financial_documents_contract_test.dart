import 'dart:convert';
import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:khadra_mobile/api/dtos.dart';
import 'package:khadra_mobile/features/invoices/document_content.dart';

/// The app's reader of issued documents against the SHARED contract fixture
/// (payments Phase 5b, decision D5): `docs/contracts/financial-documents-v1.json`
/// holds customer pages of every shape the version 1 grammar has, composed by the
/// server's own composer and pinned byte for byte by the API's
/// `FinancialDocumentFixtureTests`. The website reads the same file, so the server,
/// the website and every installed build of this app are proven against the same
/// documents. The reader's contract is written in `docs/contracts/README.md`.
void main() {
  final fixture = jsonDecode(
    File('${Directory.current.parent.path}/docs/contracts/financial-documents-v1.json').readAsStringSync(),
  ) as Map<String, dynamic>;
  final entries = (fixture['documents'] as List<dynamic>).cast<Map<String, dynamic>>();
  Map<String, dynamic> pageJson(String name) =>
      jsonDecode(jsonEncode(entries.firstWhere((entry) => entry['name'] == name)['page'])) as Map<String, dynamic>;
  Map<String, dynamic> snapshotOf(String name) => pageJson(name)['snapshot'] as Map<String, dynamic>;
  Map<String, dynamic> contentOf(Map<String, dynamic> snapshot) => snapshot['content'] as Map<String, dynamic>;
  List<Map<String, dynamic>> sectionsOf(Map<String, dynamic> snapshot) =>
      (contentOf(snapshot)['sections'] as List<dynamic>).cast<Map<String, dynamic>>();
  List<Map<String, dynamic>> linesOf(Map<String, dynamic> section) =>
      (section['lines'] as List<dynamic>).cast<Map<String, dynamic>>();

  group('every document the server composes', () {
    test('parses as a page and reads whole, sections and lines in their stored order', () {
      expect(entries, isNotEmpty);
      for (final entry in entries) {
        final name = entry['name'] as String;
        final page = FinancialDocumentPage.fromJson(entry['page'] as Map<String, dynamic>);
        final content = DocumentContent.tryParse(page.snapshotSchemaVersion, page.snapshot);
        expect(content, isNotNull, reason: name);

        final stored = sectionsOf(page.snapshot! as Map<String, dynamic>);
        expect([for (final section in content!.sections) section.key], [for (final section in stored) section['key']], reason: name);
        for (final (index, section) in content.sections.indexed) {
          expect([for (final line in section.lines) line.key], [for (final line in linesOf(stored[index])) line['key']], reason: name);
        }
      }
    });

    test('meet every kind of value, and a line with no label — so the fixture covers the grammar', () {
      final kinds = <Type>{};
      var unlabelled = 0;
      for (final entry in entries) {
        final page = FinancialDocumentPage.fromJson(entry['page'] as Map<String, dynamic>);
        for (final section in DocumentContent.tryParse(page.snapshotSchemaVersion, page.snapshot)!.sections) {
          for (final line in section.lines) {
            kinds.add(line.value.runtimeType);
            if (line.label == null) unlabelled++;
          }
        }
      }
      expect(kinds, {MoneyValue, InstantValue, TextValue, PlainValue});
      expect(unlabelled, greaterThan(0));
    });

    test('keep an amount as the string it was stored as', () {
      final content = DocumentContent.tryParse(1, snapshotOf('payment-receipt-paid-in-full'))!;
      expect(content.headlineAmount, '94.500');
      expect(content.headlineCurrency, 'JOD');
      expect(content.title.ar, 'إيصال دفع');
    });

    test("read a statement issued for a receipt's correction: its cause in both languages, the correction alone listed", () {
      final content = DocumentContent.tryParse(1, snapshotOf('booking-statement-receipt-corrected'))!;
      DocumentSection section(String key) => content.sections.firstWhere((candidate) => candidate.key == key);

      final cause = section('document').lines.firstWhere((line) => line.key == 'cause').value as TextValue;
      expect((cause.text.en, cause.text.ar), ('Receipt corrected', 'تصحيح إيصال'));
      final listed = [
        for (final line in section('documents').lines)
          if (line.value case PlainValue(:final plain)) plain,
      ];
      expect(listed, contains('TEST-PAY-2026-000007'));
      expect(listed, isNot(contains('TEST-PAY-2026-000006')));
    });

    test('carry their standing, links and void as the endpoint sends them', () {
      final voided = FinancialDocumentPage.fromJson(pageJson('payment-receipt-deposit-voided'));
      expect(voided.row.status, 'Voided');
      expect(voided.voided?.replacedBy?.number, 'TEST-PAY-2026-000005');
      expect(voided.links.versions.map((link) => link.status), ['Voided', 'Current']);

      final refund = FinancialDocumentPage.fromJson(pageJson('refund-receipt-free-cancellation'));
      expect(refund.links.paymentReceipt?.number, 'TEST-PAY-2026-000002');
      expect(refund.row.headline.amount, 94.5);
    });

    test('lists and a booking’s documents parse as the endpoints send them', () {
      final list = Paged.fromJson(fixture['myDocuments'] as Map<String, dynamic>, FinancialDocumentRow.fromJson);
      expect(list.items, hasLength(list.totalCount));
      expect(list.items.first.title.en, isNotEmpty);

      final booking = BookingFinancialDocuments.fromJson(fixture['bookingDocuments'] as Map<String, dynamic>);
      expect(booking.documents, isNotEmpty);
      expect(booking.beingPrepared.single.type, FinancialDocumentTypes.bookingStatement);
      expect(booking.beingPrepared.single.occurredAt, isNotNull);
    });
  });

  group('what the reader tolerates (what the server may do within version 1)', () {
    test('ignores keys it does not know, anywhere', () {
      final snapshot = snapshotOf('booking-statement-dispute-decided');
      final before = DocumentContent.tryParse(1, snapshot)!;
      snapshot['somethingNew'] = {'any': 'thing'};
      contentOf(snapshot)['layoutHint'] = 'two-columns';
      sectionsOf(snapshot).first['collapsed'] = true;
      linesOf(sectionsOf(snapshot).first).first['emphasis'] = 'strong';
      final after = DocumentContent.tryParse(1, snapshot)!;
      expect(after.sections.length, before.sections.length);
      expect(after.sections.first.lines.length, before.sections.first.lines.length);
    });

    test('renders sections it has never seen, in the order given', () {
      final snapshot = snapshotOf('payment-receipt-paid-in-full');
      final sections = sectionsOf(snapshot);
      sections.first['key'] = 'aSectionBornLater';
      contentOf(snapshot)['sections'] = sections.reversed.toList();
      final content = DocumentContent.tryParse(1, snapshot)!;
      expect(content.sections.last.key, 'aSectionBornLater');
    });

    test('reads an absent label, time note or notice as nothing — and writes none itself', () {
      final snapshot = snapshotOf('payment-receipt-not-applied');
      contentOf(snapshot).remove('timeNote');
      contentOf(snapshot).remove('notice');
      linesOf(sectionsOf(snapshot).first).first.remove('label');
      final content = DocumentContent.tryParse(1, snapshot)!;
      expect(content.timeNote, isNull);
      expect(content.notice, isNull);
      expect(content.sections.first.lines.first.label, isNull);
    });

    test('counts a value key holding null as no value', () {
      final snapshot = snapshotOf('refund-receipt-free-cancellation');
      final line = linesOf(sectionsOf(snapshot).first).first;
      line['text'] = null;
      expect(DocumentContent.tryParse(1, snapshot), isNotNull);
    });

    test('still reads an amount or a time off its pattern, for Formats to print as it is', () {
      final snapshot = snapshotOf('payment-receipt-paid-in-full');
      ((contentOf(snapshot)['headline'] as Map<String, dynamic>)['money'] as Map<String, dynamic>)['amount'] = '94,5';
      final instant = linesOf(sectionsOf(snapshot).first).firstWhere((line) => line['instant'] != null);
      (instant['instant'] as Map<String, dynamic>)['local'] = '2026-9-3 15:01';
      final content = DocumentContent.tryParse(1, snapshot)!;
      expect(content.headlineAmount, '94,5');
      final read = content.sections.first.lines.map((line) => line.value).whereType<InstantValue>().first;
      expect(read.local, '2026-9-3 15:01');
    });
  });

  group('what the reader refuses whole — a record is never shown with a line missing', () {
    test('a schema version it does not know, gating on the page and never on the snapshot', () {
      expect(DocumentContent.tryParse(2, snapshotOf('payment-receipt-paid-in-full')), isNull);
      final snapshot = snapshotOf('payment-receipt-paid-in-full')..['schemaVersion'] = 99;
      expect(DocumentContent.tryParse(1, snapshot), isNotNull);
    });

    test('a line with no value, or with two', () {
      final none = snapshotOf('refund-receipt-free-cancellation');
      final bare = linesOf(sectionsOf(none).first).first;
      for (final kind in ['money', 'instant', 'text', 'plain']) {
        bare.remove(kind);
      }
      expect(DocumentContent.tryParse(1, none), isNull);

      final two = snapshotOf('refund-receipt-free-cancellation');
      linesOf(sectionsOf(two).first).first
        ..['plain'] = 'x'
        ..['text'] = {'en': 'x', 'ar': 'س'};
      expect(DocumentContent.tryParse(1, two), isNull);
    });

    test('a missing title, headline, heading, lines or line key', () {
      final breaks = <void Function(Map<String, dynamic>)>[
        (snapshot) => contentOf(snapshot).remove('title'),
        (snapshot) => (contentOf(snapshot)['headline'] as Map<String, dynamic>).remove('money'),
        (snapshot) => (contentOf(snapshot)['headline'] as Map<String, dynamic>).remove('label'),
        (snapshot) => sectionsOf(snapshot)[1].remove('heading'),
        (snapshot) => sectionsOf(snapshot)[1].remove('lines'),
        (snapshot) => linesOf(sectionsOf(snapshot)[1]).first.remove('key'),
        (snapshot) => contentOf(snapshot)['sections'] = 'not a list',
      ];
      for (final breakIt in breaks) {
        final snapshot = snapshotOf('booking-statement-cash-at-handover');
        breakIt(snapshot);
        expect(DocumentContent.tryParse(1, snapshot), isNull);
      }
    });

    test('a text that is not two strings, and an amount or a literal that is not a string', () {
      final text = snapshotOf('payment-receipt-deposit-correction');
      (contentOf(text)['title'] as Map<String, dynamic>)['ar'] = 7;
      expect(DocumentContent.tryParse(1, text), isNull);

      final amount = snapshotOf('payment-receipt-deposit-correction');
      ((contentOf(amount)['headline'] as Map<String, dynamic>)['money'] as Map<String, dynamic>)['amount'] = 19.5;
      expect(DocumentContent.tryParse(1, amount), isNull);

      final plain = snapshotOf('payment-receipt-deposit-correction');
      linesOf(sectionsOf(plain).first).firstWhere((line) => line['plain'] != null)['plain'] = 7;
      expect(DocumentContent.tryParse(1, plain), isNull);
    });

    test('a label that is not a text, and a time missing either half', () {
      Map<String, dynamic> labelled(Map<String, dynamic> snapshot) =>
          linesOf(sectionsOf(snapshot).first).firstWhere((line) => line['label'] != null);
      Map<String, dynamic> instant(Map<String, dynamic> snapshot) => sectionsOf(snapshot)
          .expand(linesOf)
          .firstWhere((line) => line['instant'] != null)['instant'] as Map<String, dynamic>;

      final breaks = <void Function(Map<String, dynamic>)>[
        (snapshot) => labelled(snapshot)['label'] = 'Amount paid',
        (snapshot) => (labelled(snapshot)['label'] as Map<String, dynamic>).remove('ar'),
        (snapshot) => instant(snapshot).remove('local'),
        (snapshot) => instant(snapshot).remove('utc'),
        (snapshot) => instant(snapshot)['local'] = 20260903,
      ];
      for (final (index, breakIt) in breaks.indexed) {
        final snapshot = snapshotOf('payment-receipt-paid-in-full');
        expect(DocumentContent.tryParse(1, snapshot), isNotNull, reason: 'the untouched document, case $index');
        breakIt(snapshot);
        expect(DocumentContent.tryParse(1, snapshot), isNull, reason: 'case $index');
      }
    });

    test('what is not a document at all', () {
      expect(DocumentContent.tryParse(1, null), isNull);
      expect(DocumentContent.tryParse(1, 'a document'), isNull);
      expect(DocumentContent.tryParse(1, <String, dynamic>{}), isNull);
    });
  });
}
