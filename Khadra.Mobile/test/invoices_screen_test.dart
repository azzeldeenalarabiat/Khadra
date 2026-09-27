import 'dart:convert';
import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intl/date_symbol_data_local.dart';
import 'package:khadra_mobile/api/dtos.dart';
import 'package:khadra_mobile/core/providers.dart';
import 'package:khadra_mobile/core/theme/khadra_theme.dart';
import 'package:khadra_mobile/features/invoices/invoice_content.dart';
import 'package:khadra_mobile/features/invoices/invoice_screen.dart';
import 'package:khadra_mobile/features/invoices/invoices_screen.dart';
import 'package:khadra_mobile/l10n/app_localizations.dart';
import 'package:timezone/data/latest_all.dart' as tz_data;

import 'support/fake_api.dart';

/// Invoices & Receipts and a document's screen (payments Phase 5b), rendering the
/// documents of the SHARED contract fixture — every shape the server composes —
/// at the widths the app is measured at, in both languages.
void main() {
  setUpAll(() async {
    tz_data.initializeTimeZones();
    await initializeDateFormatting('en');
    await initializeDateFormatting('ar');
  });

  final fixture = jsonDecode(
    File('${Directory.current.parent.path}/docs/contracts/financial-documents-v1.json').readAsStringSync(),
  ) as Map<String, dynamic>;
  final entries = (fixture['documents'] as List<dynamic>).cast<Map<String, dynamic>>();
  final pages = {
    for (final entry in entries)
      entry['name'] as String: FinancialDocumentPage.fromJson(entry['page'] as Map<String, dynamic>),
  };
  final rows = Paged.fromJson(fixture['myDocuments'] as Map<String, dynamic>, FinancialDocumentRow.fromJson).items;
  String plain(String text) => text.replaceAll('\u2068', '').replaceAll('\u2069', '');

  Future<FakeApi> pump(
    WidgetTester tester,
    Widget screen, {
    required Locale locale,
    double width = 412,
    double height = 915,
    List<FinancialDocumentRow>? documents,
    Map<String, FinancialDocumentPage>? byId,
  }) async {
    tester.view.physicalSize = Size(width, height);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);

    final api = FakeApi()
      ..myFinancialDocumentRows = documents ?? rows
      ..documentsById = byId ?? {for (final page in pages.values) page.row.documentId: page};
    final container = ProviderContainer(overrides: [
      apiProvider.overrideWithValue(api),
      sessionStoreProvider.overrideWithValue(FakeSessionStore()),
      sharedPreferencesProvider.overrideWithValue(null),
      isArabicProvider.overrideWithValue(locale.languageCode == 'ar'),
    ]);
    addTearDown(container.dispose);
    // The app's own language, not only the framework's: `formatsProvider` follows it,
    // and a document is read in the language `Formats` names — as it is in the app,
    // where both come from the one switch.
    await container.read(localeProvider.notifier).set(locale);
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
          home: screen,
        ),
      ),
    );
    await tester.pumpAndSettle();
    return api;
  }

  void screenTest(String name, Future<void> Function(WidgetTester) body) {
    testWidgets(name, (tester) async {
      await body(tester);
      await tester.pumpWidget(const SizedBox());
      await tester.pump();
    });
  }

  for (final locale in const [Locale('en'), Locale('ar')]) {
    final tag = locale.languageCode;
    final l10n = lookupAppLocalizations(locale);

    for (final width in [360.0, 375.0, 412.0]) {
      screenTest('Invoices & Receipts lists every version, marked, at ${width.toInt()} in $tag', (tester) async {
        await pump(tester, const InvoicesScreen(), locale: locale, width: width);

        expect(tester.takeException(), isNull, reason: 'overflow at $width in $tag');
        expect(find.text(l10n.invoicesTitle), findsOneWidget);
        expect(find.text(rows.first.number), findsOneWidget);
        // The voided receipt was issued first, so it is the last row: scrolled to, it is marked.
        final voided = find.text(l10n.invoicesStatusVoided);
        await tester.scrollUntilVisible(voided, 300, scrollable: find.byWidgetPredicate(
          (widget) => widget is Scrollable && widget.axisDirection == AxisDirection.down,
        ));
        expect(voided, findsWidgets);
        expect(tester.takeException(), isNull);
      });

      for (final name in pages.keys) {
        screenTest('the $name document reads whole at ${width.toInt()} in $tag', (tester) async {
          await pump(tester, InvoiceScreen(documentId: pages[name]!.row.documentId), locale: locale, width: width);

          expect(tester.takeException(), isNull, reason: 'overflow in $name at $width in $tag');
          final notice = tag == 'ar' ? 'هذا المستند ليس فاتورة ضريبية.' : 'This document is not a tax invoice.';
          await tester.scrollUntilVisible(find.text(notice), 300, scrollable: find.byType(Scrollable).first);
          expect(find.text(notice), findsOneWidget);
        });
      }
    }

    screenTest('a filter asks the server for its kind, and says when there is none of it, in $tag', (tester) async {
      final api = await pump(tester, const InvoicesScreen(), locale: locale, documents: const []);

      expect(find.text(l10n.invoicesEmpty), findsOneWidget);
      // The chips scroll sideways: bring this one into view, as a thumb would.
      await tester.ensureVisible(find.text(l10n.invoicesFilterRefundReceipt));
      await tester.pumpAndSettle();
      await tester.tap(find.text(l10n.invoicesFilterRefundReceipt));
      await tester.pumpAndSettle();
      expect(api.myFinancialDocumentTypes.last, FinancialDocumentTypes.refundReceipt);
      expect(find.text(l10n.invoicesEmptyFilter), findsOneWidget);
    });

    screenTest('a voided receipt says so and links its correction, never the reason, in $tag', (tester) async {
      await pump(tester, InvoiceScreen(documentId: pages['payment-receipt-deposit-voided']!.row.documentId), locale: locale);

      expect(find.byWidgetPredicate((widget) => widget is Text && plain(widget.data ?? '').contains('TEST-PAY-2026-000005')), findsWidgets);
      expect(find.textContaining('wrong office location'), findsNothing);
    });

    screenTest('an earlier version links the newest, in $tag', (tester) async {
      await pump(tester, InvoiceScreen(documentId: pages['booking-statement-superseded']!.row.documentId), locale: locale);

      // The approved sentence, split between the notice and the button that opens
      // the newest: its words above, the number once, below.
      final sentence = plain(l10n.invoicesNewerVersion('TEST-STM-2026-000003'));
      final words = sentence.substring(0, sentence.indexOf('TEST-STM-2026-000003')).trim();
      expect(find.text(words), findsOneWidget);
      expect(find.widgetWithText(TextButton, 'TEST-STM-2026-000003'), findsOneWidget);
    });

    // On a phone these sentences wrap; left to themselves they broke inside the
    // number, at a hyphen ("TEST-" / "PAY-2026-000002").
    for (final (name, number) in const [
      ('refund-receipt-free-cancellation', 'TEST-PAY-2026-000002'),
      ('payment-receipt-deposit-voided', 'TEST-PAY-2026-000005'),
      ('booking-statement-superseded', 'TEST-STM-2026-000003'),
    ]) {
      screenTest('the number in $name’s link sentence stays on one line at 360, in $tag', (tester) async {
        await pump(tester, InvoiceScreen(documentId: pages[name]!.row.documentId), locale: locale, width: 360);

        final text = find.descendant(of: find.byType(NumberRun), matching: find.text(number));
        await tester.scrollUntilVisible(text, 200, scrollable: find.byType(Scrollable).first);
        final line = tester.renderObject<RenderParagraph>(text).getFullHeightForCaret(const TextPosition(offset: 0));
        expect(tester.getSize(text).height, lessThan(line * 1.5));
      });
    }

    screenTest('a document that is not there reads as not available, in $tag', (tester) async {
      await pump(tester, const InvoiceScreen(documentId: '00000000-0000-4000-8000-000000000999'), locale: locale);

      expect(find.text(l10n.invoicesNotAvailable), findsOneWidget);
      expect(find.text(l10n.invoicesBackToList), findsOneWidget);
    });

    screenTest('on a short screen the way back scrolls into view rather than overflowing, in $tag', (tester) async {
      // A phone on its side: the browser check found the empty state overflowing
      // over its own button at 300 pixels tall.
      await pump(tester, const InvoiceScreen(documentId: '00000000-0000-4000-8000-000000000999'), locale: locale, width: 360, height: 300);

      expect(tester.takeException(), isNull);
      await tester.scrollUntilVisible(find.text(l10n.invoicesBackToList), 100, scrollable: find.byType(Scrollable).first);
      expect(find.text(l10n.invoicesBackToList).hitTestable(), findsOneWidget);
    });

    screenTest('a schema version this build does not know shows the facts it has and asks for an update, in $tag', (tester) async {
      final known = pages['payment-receipt-paid-in-full']!;
      final unknown = FinancialDocumentPage(
        row: known.row,
        snapshotSchemaVersion: 2,
        snapshot: known.snapshot,
        links: known.links,
        voided: known.voided,
      );
      await pump(
        tester,
        InvoiceScreen(documentId: known.row.documentId),
        locale: locale,
        width: 360,
        byId: {known.row.documentId: unknown},
      );

      expect(tester.takeException(), isNull);
      expect(find.text(l10n.invoicesCannotShowApp), findsOneWidget);
      expect(find.text(known.row.headlineLabel.of(arabic: tag == 'ar')), findsOneWidget);
      // Nothing of the stored document itself: it is shown whole or not at all.
      final notice = tag == 'ar' ? 'هذا المستند ليس فاتورة ضريبية.' : 'This document is not a tax invoice.';
      expect(find.text(notice), findsNothing);
    });

    screenTest('a Latin value standing without a label sits at the reading start, in $tag', (tester) async {
      // No issued document has one yet, and version 1 allows the server to add
      // one: it must not hug the LEFT edge of an Arabic page.
      final json = jsonDecode(jsonEncode(entries.firstWhere((entry) => entry['name'] == 'payment-receipt-paid-in-full')['page']))
          as Map<String, dynamic>;
      final section = ((json['snapshot'] as Map<String, dynamic>)['content'] as Map<String, dynamic>)['sections'][0]
          as Map<String, dynamic>;
      (section['lines'] as List<dynamic>).add({'key': 'test:aloneLatin', 'plain': 'REF-2026-XYZ'});
      final page = FinancialDocumentPage.fromJson(json);
      await pump(tester, InvoiceScreen(documentId: page.row.documentId), locale: locale, width: 360, byId: {page.row.documentId: page});

      final alone = find.text('REF-2026-XYZ');
      final heading = find.text((section['heading'] as Map<String, dynamic>)[tag] as String);
      await tester.scrollUntilVisible(alone, 200, scrollable: find.byType(Scrollable).first);
      expect(tester.getSize(alone).width, lessThan(tester.getSize(heading).width));
      if (tag == 'ar') {
        expect(tester.getTopRight(alone).dx, moreOrLessEquals(tester.getTopRight(heading).dx, epsilon: 0.5));
      } else {
        expect(tester.getTopLeft(alone).dx, moreOrLessEquals(tester.getTopLeft(heading).dx, epsilon: 0.5));
      }
    });
  }
}
