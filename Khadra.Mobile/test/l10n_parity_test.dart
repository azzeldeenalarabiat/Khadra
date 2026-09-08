import 'dart:convert';
import 'dart:io';

import 'package:flutter_test/flutter_test.dart';

/// The two ARB files must carry the same keys.
///
/// `flutter gen-l10n` warns about a missing translation and carries on, so a key
/// added in English alone reaches a release as English text inside an Arabic
/// interface. This is the app's version of the console typing `ar.ts` against
/// `en.ts`, which is what stopped Arabic quietly falling behind there.
void main() {
  Map<String, dynamic> read(String path) =>
      jsonDecode(File(path).readAsStringSync()) as Map<String, dynamic>;

  /// Message keys only. `@@locale` is metadata and `@key` entries are the
  /// placeholder descriptions, which live only in the template.
  Set<String> messageKeys(Map<String, dynamic> arb) =>
      arb.keys.where((key) => !key.startsWith('@')).toSet();

  late Map<String, dynamic> english;
  late Map<String, dynamic> arabic;

  setUpAll(() {
    english = read('lib/l10n/app_en.arb');
    arabic = read('lib/l10n/app_ar.arb');
  });

  test('every English key has an Arabic translation', () {
    final missing = messageKeys(english).difference(messageKeys(arabic));
    expect(
      missing,
      isEmpty,
      reason: 'Missing from app_ar.arb: ${missing.join(', ')}',
    );
  });

  test('Arabic carries no key English has dropped', () {
    final orphaned = messageKeys(arabic).difference(messageKeys(english));
    expect(
      orphaned,
      isEmpty,
      reason: 'No longer in app_en.arb: ${orphaned.join(', ')}',
    );
  });

  test('no Arabic message is left as its English source', () {
    // A copy-paste that never got translated is invisible to gen-l10n, because
    // the key is present. It is visible here.
    //
    // A few are identical by design: a language names itself in its own language
    // on BOTH sides of the switch, because somebody who has the app in the wrong
    // one has to be able to find their way out of it. And a phone-number hint is
    // digits.
    const identicalByDesign = {
      'profileLanguageEnglish',
      'profileLanguageArabic',
      'authPhoneHint',
    };

    final untranslated = <String>[];

    for (final key in messageKeys(english)) {
      if (identicalByDesign.contains(key)) continue;

      final source = english[key];
      final translation = arabic[key];
      if (source is! String || translation is! String) continue;

      if (source == translation) untranslated.add(key);
    }

    expect(
      untranslated,
      isEmpty,
      reason: 'Still in English in app_ar.arb: ${untranslated.join(', ')}',
    );
  });

  test('every declared placeholder appears in the Arabic message', () {
    // A dropped `{amount}` compiles and then renders a price-less sentence at
    // runtime — a cancellation notice with no figure in it.
    //
    // The DECLARED placeholders are the authority, taken from the template's
    // `@key` metadata. Scraping `{name}` out of the text instead would count an
    // ICU plural body like `=1{yesterday}` as a placeholder called "yesterday".
    final missing = <String>[];

    for (final key in messageKeys(english)) {
      final metadata = english['@$key'];
      if (metadata is! Map) continue;
      final declared = metadata['placeholders'];
      if (declared is! Map) continue;

      final translation = arabic[key];
      if (translation is! String) continue;

      for (final placeholder in declared.keys) {
        if (!translation.contains('{$placeholder}')) {
          missing.add('$key is missing {$placeholder}');
        }
      }
    }

    expect(missing, isEmpty, reason: missing.join('\n'));
  });

  test('no message adds a percent sign to a formatted percentage', () {
    // `Formats.percent` owns how a percentage looks, sign included. A message that
    // carried its own literal % beside one of its values rendered "Deposit (20%%)"
    // on a real booking screen, in both languages.
    //
    // `bookingFuelLevel` is exempt: it is handed a bare number, because a fuel
    // gauge is not a business rule and does not go through that formatter.
    const takesFormattedPercentage = {
      'bookDepositNow',
      'bookTermsCancellationPenalty',
      'cancelPenaltyNotice',
    };

    final doubled = <String>[];
    for (final arb in [english, arabic]) {
      for (final key in takesFormattedPercentage) {
        final message = arb[key];
        if (message is String && message.contains('{percent}%')) {
          doubled.add('$key (${arb['@@locale']})');
        }
      }
    }

    expect(doubled, isEmpty, reason: 'Doubled percent sign in: ${doubled.join(', ')}');
  });

  test('every Arabic plural covers the categories the language has', () {
    // Arabic has six plural categories. A translation that only spells out
    // `other` renders "2 سيارة" where the language wants "سيارتان", and gen-l10n
    // will not say a word about it.
    final plurals = <String>[];

    for (final key in messageKeys(english)) {
      final source = english[key];
      final translation = arabic[key];
      if (source is! String || translation is! String) continue;
      if (!source.contains(', plural,')) continue;

      final hasTwo = translation.contains('=2{');
      final hasFew = translation.contains('few{');
      final hasMany = translation.contains('many{');

      if (!hasTwo || !hasFew || !hasMany) {
        plurals.add(key);
      }
    }

    expect(
      plurals,
      isEmpty,
      reason: 'Arabic plurals missing =2/few/many: ${plurals.join(', ')}',
    );
  });
}
