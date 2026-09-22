import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:khadra_mobile/api/dtos.dart';
import 'package:khadra_mobile/core/api/language_interceptor.dart';
import 'package:khadra_mobile/core/widgets/khadra_widgets.dart';

/// Dealer-authored text, in two languages, as this app asks for it and shows it.
///
/// Three things are being held in place here and each of them fails silently:
///
/// - the app asks for the language it is RENDERING, so the paragraph that comes
///   back matches the screen it lands on. Get this wrong and one section of a car's
///   page arrives in the other language for no reason the reader can see;
/// - the answer's own language travels with the text, because the server sends what
///   the office actually wrote rather than an empty heading — so the text is
///   sometimes the other language, and nothing else on the wire says so;
/// - direction still comes from the CHARACTERS, not from that language, because an
///   office may type Arabic into the English box and the paragraph has to read
///   correctly either way.
void main() {
  group('the language this app asks for', () {
    /// Runs one request through the interceptor and gives back what it would send.
    Map<String, dynamic> headersFor(
      String language, {
      Map<String, dynamic> already = const {},
    }) {
      final options = RequestOptions(path: '/catalogue', headers: {...already});
      LanguageInterceptor(() => language)
          .onRequest(options, RequestInterceptorHandler());
      return options.headers;
    }

    test('names the app language on every request', () {
      expect(headersFor('ar')['Accept-Language'], 'ar');
      expect(headersFor('en')['Accept-Language'], 'en');
    });

    test('is read at request time, so a switch takes effect on the next call', () {
      // The reason this is a callback and not a header baked into BaseOptions: the
      // switch in Profile turns the app without a restart, and a value captured at
      // construction would keep asking for the language the app opened in.
      var language = 'en';
      final interceptor = LanguageInterceptor(() => language);

      final first = RequestOptions(path: '/catalogue');
      interceptor.onRequest(first, RequestInterceptorHandler());
      expect(first.headers['Accept-Language'], 'en');

      language = 'ar';
      final second = RequestOptions(path: '/catalogue');
      interceptor.onRequest(second, RequestInterceptorHandler());
      expect(second.headers['Accept-Language'], 'ar');
    });

    test('leaves a header a caller set for itself', () {
      expect(headersFor('ar', already: {'Accept-Language': 'en'})['Accept-Language'],
          'en');
      // Whatever case it was written in: HTTP header names are not case-sensitive
      // and a second one would be sent alongside the first.
      expect(headersFor('ar', already: {'accept-language': 'en'})['accept-language'],
          'en');
      expect(headersFor('ar', already: {'accept-language': 'en'}).keys.length, 1);
    });
  });

  group('a resolved piece of an office’s writing', () {
    test('carries the language the server said it is in', () {
      final text = ResolvedText.maybe({'text': 'No smoking.', 'language': 'en'});

      expect(text!.text, 'No smoking.');
      expect(text.language, 'en');
    });

    test('keeps a language that is NOT the one asked for', () {
      // The fallback, which is the whole reason the language is on the wire: an
      // Arabic reader is shown the English the office actually wrote.
      final text = ResolvedText.maybe({
        'text': 'Comprehensive, 200 JOD excess.',
        'language': 'en',
      });

      expect(text!.language, 'en');
    });

    test('is null for anything with nothing to read', () {
      // Absent, blank, whitespace, and not an object at all: a section with nothing
      // in it is not a section, and the screen renders no heading for one.
      expect(ResolvedText.maybe(null), isNull);
      expect(ResolvedText.maybe({'text': null, 'language': 'ar'}), isNull);
      expect(ResolvedText.maybe({'text': '', 'language': 'ar'}), isNull);
      expect(ResolvedText.maybe({'text': '   ', 'language': 'ar'}), isNull);
      expect(ResolvedText.maybe('just a string'), isNull);
    });

    test('never guesses the language from the app’s own', () {
      // English, not "whatever this phone is showing". A paragraph claiming to be
      // Arabic because the reader is Arabic would be read aloud in the wrong voice.
      final text = ResolvedText.maybe({'text': 'Family-run since 2014.'});

      expect(text!.language, 'en');
    });
  });

  group('the six sections of a gallery page', () {
    final sections = GallerySections.fromJson({
      'about': {'text': 'مكتب عائلي منذ 2014.', 'language': 'ar'},
      'rentalConditions': {'text': 'No smoking.', 'language': 'en'},
      'insurance': null,
      'pickupInstructions': {'text': '   ', 'language': 'ar'},
      'deliveryNotes': {'text': 'نوصل إلى المطار.', 'language': 'ar'},
      'customerNotes': null,
    });

    test('reads each section with its own language', () {
      expect(sections.about!.language, 'ar');
      expect(sections.rentalConditions!.language, 'en');
      expect(sections.deliveryNotes!.text, 'نوصل إلى المطار.');
    });

    test('shows nothing at all for a section with nothing in it', () {
      // Null says nothing about why — hidden, never written, and an office that
      // does not deliver all arrive the same way, and the app must never ask.
      expect(sections.insurance, isNull);
      expect(sections.customerNotes, isNull);
      expect(sections.pickupInstructions, isNull);
    });
  });

  group('how a resolved paragraph is drawn', () {
    testWidgets('takes its DIRECTION from the characters, not from the language',
        (tester) async {
      // An office typing Arabic into the English box is the case. The text is
      // Arabic and has to read right-to-left whatever the pair claims — the
      // direction is a property of the characters and the claim is not a
      // measurement of them.
      await tester.pumpWidget(MaterialApp(
        home: UserText.resolved(
          const ResolvedText('ممنوع التدخين في أي سيارة.', 'en'),
        ),
      ));

      expect(tester.widget<Text>(find.byType(Text)).textDirection,
          TextDirection.rtl);
    });

    testWidgets('gives the text the language the server claimed, for the voice',
        (tester) async {
      // The locale is what a screen reader uses to choose how to pronounce a
      // paragraph. An English fallback inside an Arabic app must not be sounded
      // out in Arabic.
      await tester.pumpWidget(const MaterialApp(
        home: UserText('Comprehensive, 200 JOD excess.', language: 'en'),
      ));

      final text = tester.widget<Text>(find.byType(Text));
      expect(text.locale, const Locale('en'));
      expect(text.textDirection, TextDirection.ltr);
    });

    testWidgets('claims no language for text whose language nothing stated',
        (tester) async {
      // A customer's own dispute statement. Nothing has said what language it is
      // in, and inventing one would be a claim the platform cannot make.
      await tester.pumpWidget(const MaterialApp(home: UserText('انتظرت ساعة.')));

      final text = tester.widget<Text>(find.byType(Text));
      expect(text.locale, isNull);
      expect(text.textDirection, TextDirection.rtl);
    });
  });
}
