import 'package:flutter/widgets.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:khadra_mobile/core/providers.dart';
import 'package:khadra_mobile/l10n/app_localizations.dart';

/// Which language a device gets, and the agreement that has to hold underneath it.
///
/// Two answers to one question is the bug this file exists for. `MaterialApp` with
/// no `locale` resolves through `basicLocaleListResolution`, which falls back to
/// `supportedLocales.first` — a list generated from the ARB FILE NAMES, so `[ar,
/// en]` by alphabet. A phone set to Turkish therefore opened this app in Arabic,
/// laid out right to left, for no reason anybody chose. Meanwhile the providers
/// that render city names, car types and dates read
/// `platformDispatcher.locale.languageCode`, saw `tr`, and rendered that same
/// Arabic screen's data in English.
///
/// Neither half was visibly wrong in Arabic or in English, which is why it
/// survived: it only shows on a device asking for a third language.
void main() {
  test('a device asking for Arabic gets Arabic', () {
    expect(
      resolveKhadraLocale(null, const [Locale('ar'), Locale('en')]).languageCode,
      'ar',
    );
    expect(
      resolveKhadraLocale(null, const [Locale('ar', 'JO')]).languageCode,
      'ar',
    );
  });

  test('a device asking for English gets English', () {
    expect(
      resolveKhadraLocale(null, const [Locale('en', 'GB')]).languageCode,
      'en',
    );
  });

  test('a device asking for neither gets English, not whatever sorts first', () {
    // The case that was wrong. A visitor to Jordan with a Turkish, French or
    // Russian phone was handed a right-to-left Arabic interface because `ar`
    // precedes `en` in the alphabet.
    for (final device in const [
      [Locale('tr')],
      [Locale('fr', 'FR')],
      [Locale('ru')],
      [Locale('fil')],
      <Locale>[],
    ]) {
      expect(
        resolveKhadraLocale(null, device).languageCode,
        'en',
        reason: 'a device asking for ${device.isEmpty ? 'nothing' : device.first} '
            'should be read in English',
      );
    }
  });

  test('the order the device asks in is respected', () {
    // Android and iOS both send a LIST, most-preferred first. Somebody whose
    // phone prefers French but also lists Arabic should get Arabic, not English.
    expect(
      resolveKhadraLocale(null, const [Locale('fr'), Locale('ar')]).languageCode,
      'ar',
    );
    expect(
      resolveKhadraLocale(null, const [Locale('fr'), Locale('en'), Locale('ar')])
          .languageCode,
      'en',
    );
  });

  test('a language chosen in Profile beats the device', () {
    // The switch in Profile is the customer saying it outright, and nothing the
    // device reports may override that.
    expect(
      resolveKhadraLocale(const Locale('ar'), const [Locale('en')]).languageCode,
      'ar',
    );
    expect(
      resolveKhadraLocale(const Locale('en'), const [Locale('ar')]).languageCode,
      'en',
    );
  });

  test('every answer it can give is a language the app actually has', () {
    // A locale resolved to something with no ARB file behind it renders the
    // fallback language with none of the plural rules, which is worse than
    // choosing wrongly between the two.
    final supported =
        AppLocalizations.supportedLocales.map((l) => l.languageCode).toSet();

    for (final device in const [
      [Locale('ar')],
      [Locale('en')],
      [Locale('tr')],
      <Locale>[],
    ]) {
      expect(supported, contains(resolveKhadraLocale(null, device).languageCode));
    }
  });
}
