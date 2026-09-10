import 'dart:convert';
import 'dart:io';

import 'package:dio/dio.dart';
import 'package:flutter/widgets.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:khadra_mobile/api/dtos.dart';
import 'package:khadra_mobile/core/api/api_failure.dart';
import 'package:khadra_mobile/core/api/api_failure_messages.dart';
import 'package:khadra_mobile/l10n/app_localizations.dart';

/// Every code the app claims to translate must be a code the SERVER emits.
///
/// This exists because three of them were not. `auth.weak_password`,
/// `auth.underage` and `dispute.window_closed` had never been emitted by this
/// platform — the real codes are `auth.password_policy`,
/// `auth.under_minimum_age` and `dispute.booking_not_disputable` — so a refused
/// password, an under-age registration and a booking past its dispute window all
/// fell through to the server's English, including for an Arabic reader. Nothing
/// failed, nothing logged, and the only way to notice was to read the screen.
///
/// The check reads the domain's own `*Errors.cs` files, so a code renamed on the
/// server breaks this test rather than a customer's screen.
void main() {
  final repositoryRoot = Directory.current.parent;

  Set<String> serverCodes() {
    final codes = <String>{};
    final pattern = RegExp(r'"([a-z]+\.[a-z_]+)"');

    for (final directory in const ['Khadra.Domain', 'Khadra.Application']) {
      final root = Directory('${repositoryRoot.path}/$directory');
      if (!root.existsSync()) continue;
      for (final file in root.listSync(recursive: true).whereType<File>()) {
        if (!file.path.endsWith('.cs')) continue;
        for (final match in pattern.allMatches(file.readAsStringSync())) {
          codes.add(match.group(1)!);
        }
      }
    }
    return codes;
  }

  /// The codes `_byCode` matches on, read from the source rather than from a
  /// second list that could fall out of step with it.
  Set<String> appCodes() {
    final source =
        File('lib/core/api/api_failure_messages.dart').readAsStringSync();
    // Only the switch arms: `'some.code' =>` or `'some.code' ||`.
    final pattern = RegExp(r"'([a-z]+\.[a-z_]+)'\s*(=>|\|\|)");
    return pattern.allMatches(source).map((m) => m.group(1)!).toSet();
  }

  test('every code the app translates is one the server emits', () {
    final unknown = appCodes().difference(serverCodes());
    expect(
      unknown,
      isEmpty,
      reason: 'These codes are translated by the app and emitted by nothing: '
          '${unknown.join(', ')}. A code that does not exist means the failure '
          'it covers shows the server\'s English instead.',
    );
  });

  group('messages', () {
    late AppLocalizations en;
    late AppLocalizations ar;

    setUpAll(() async {
      en = await AppLocalizations.delegate.load(const Locale('en'));
      ar = await AppLocalizations.delegate.load(const Locale('ar'));
    });

    ApiFailure failure(String code, {int status = 400, String? title}) =>
        ApiFailure.from(DioException(
          requestOptions: RequestOptions(path: '/'),
          type: DioExceptionType.badResponse,
          response: Response<dynamic>(
            requestOptions: RequestOptions(path: '/'),
            statusCode: status,
            data: {'code': code, if (title != null) 'title': title},
          ),
        ));

    /// The three that were broken, and a sample of the ones that had no wording.
    const mustBeTranslated = <String>[
      'auth.password_policy',
      'auth.under_minimum_age',
      'dispute.booking_not_disputable',
      'booking.pickup_outside_opening_hours',
      'booking.delivery_out_of_range',
      'booking.account_cannot_book',
      'documents.too_large',
      'review.window_closed',
    ];

    test('are Arabic in Arabic, and never the server\'s English', () {
      // A sentence the server would have supplied, so a fall-through is visible
      // rather than looking like a translation.
      const serverEnglish = 'THE SERVERS OWN ENGLISH SENTENCE';

      for (final code in mustBeTranslated) {
        final arabic = failure(code, title: serverEnglish).messageFor(ar);
        final english = failure(code, title: serverEnglish).messageFor(en);

        expect(arabic, isNot(serverEnglish), reason: '$code fell through in ar');
        expect(english, isNot(serverEnglish), reason: '$code fell through in en');
        expect(arabic, isNot(english), reason: '$code is not translated');
        expect(
          RegExp(r'[؀-ۿ]').hasMatch(arabic),
          isTrue,
          reason: '$code has no Arabic characters in its Arabic message',
        );
      }
    });

    test('a figure the platform published is named in the message', () {
      final config = AppConfig.fromJson(
        jsonDecode(jsonEncode({
          'minimumRenterAge': 21,
          'maxAdvanceBookingDays': 180,
          'minimumBookingLeadTimeMinutes': 120,
          'maxRentalDays': 90,
        })) as Map<String, dynamic>,
      );

      expect(
        failure('auth.under_minimum_age').messageFor(en, config: config),
        contains('21'),
      );
      expect(
        failure('booking.beyond_horizon').messageFor(en, config: config),
        contains('180'),
      );
      expect(
        failure('booking.rental_too_long').messageFor(en, config: config),
        contains('90'),
      );
    });

    test('without the config the same codes still get a sentence', () {
      // The config can legitimately be absent — a deep link can render before it
      // arrives — and a figure-less sentence beats the server's English.
      for (final code in const [
        'auth.under_minimum_age',
        'booking.beyond_horizon',
        'booking.rental_too_long',
        'booking.too_soon',
      ]) {
        final message = failure(code, title: 'ENGLISH').messageFor(ar);
        expect(message, isNot('ENGLISH'), reason: code);
        expect(RegExp(r'[؀-ۿ]').hasMatch(message), isTrue, reason: code);
      }
    });

    test('a transport failure never shows a code-based message', () {
      final offline = ApiFailure.from(DioException(
        requestOptions: RequestOptions(path: '/'),
        type: DioExceptionType.connectionError,
      ));

      // Telling somebody their password is wrong when the request never left the
      // phone is worse than saying nothing useful.
      expect(offline.messageFor(en), en.errorOffline);
      expect(offline.messageFor(ar), ar.errorOffline);
    });

    test('an unknown code falls back to the server sentence, then the trace', () {
      expect(
        failure('something.nobody_has_heard_of', title: 'A server sentence.')
            .messageFor(en),
        'A server sentence.',
      );

      final bare = ApiFailure.from(DioException(
        requestOptions: RequestOptions(path: '/'),
        type: DioExceptionType.badResponse,
        response: Response<dynamic>(
          requestOptions: RequestOptions(path: '/'),
          statusCode: 400,
          data: {'code': 'something.else', 'traceId': '00-abc-123'},
        ),
      ));
      expect(bare.messageFor(en), contains('00-abc-123'));
    });
  });
}
