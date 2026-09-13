import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:khadra_mobile/core/theme/khadra_theme.dart';
import 'package:khadra_mobile/l10n/app_localizations_ar.dart';
import 'package:khadra_mobile/l10n/app_localizations_en.dart';

/// The launcher icon, which nothing else in this suite can see.
///
/// It is generated — `tools/make_launcher_icons.dart` then `flutter_launcher_icons`
/// — and generated output is exactly the kind that rots quietly: a `flutter clean`
/// that takes the `res` folder with it, a merge that keeps one half of the change,
/// or a colour edited in the token file and not in the Gradle resource. None of
/// that fails a build. The app installs with the green Android robot on it and
/// nobody notices until it is on a phone in somebody's hand.
///
/// So these read the actual files Gradle will package.
void main() {
  const res = 'android/app/src/main/res';

  /// Every density Android asks for. A missing one is not a build error — Android
  /// scales a neighbour — but it is a blurry icon on exactly the phones that were
  /// not used for testing.
  const densities = ['mdpi', 'hdpi', 'xhdpi', 'xxhdpi', 'xxxhdpi'];

  test('every density has a legacy icon and an adaptive foreground', () {
    for (final density in densities) {
      expect(
        File('$res/mipmap-$density/ic_launcher.png').existsSync(),
        isTrue,
        reason: 'mipmap-$density/ic_launcher.png is missing — re-run '
            '`dart run tools/make_launcher_icons.dart && dart run flutter_launcher_icons`',
      );
      expect(
        File('$res/drawable-$density/ic_launcher_foreground.png').existsSync(),
        isTrue,
        reason: 'drawable-$density/ic_launcher_foreground.png is missing',
      );
    }
  });

  test('Android 8 and later get a two-layer adaptive icon', () {
    final xml = File('$res/mipmap-anydpi-v26/ic_launcher.xml');
    expect(xml.existsSync(), isTrue,
        reason: 'without this every modern launcher masks the flat PNG itself, '
            'which crops the wordmark on a circle');

    final source = xml.readAsStringSync();
    expect(source, contains('<adaptive-icon'));
    expect(source, contains('@color/ic_launcher_background'));
    expect(source, contains('@drawable/ic_launcher_foreground'));
  });

  test('the icon background is the brand green, not a hex somebody retyped', () {
    // Three files carry this colour: the Dart token, the constant in the icon
    // script, and the Gradle resource the generator writes. A Gradle resource
    // cannot import a Dart file, so the only thing keeping them in step is this.
    final colours =
        File('$res/values/colors.xml').readAsStringSync();
    final match = RegExp(r'name="ic_launcher_background">\s*#([0-9A-Fa-f]{6})')
        .firstMatch(colours);

    expect(match, isNotNull,
        reason: 'ic_launcher_background is not defined in values/colors.xml');

    final fromResource = int.parse(match!.group(1)!, radix: 16);
    final fromTokens = KhadraColors.price.toARGB32() & 0xFFFFFF;

    expect(
      fromResource,
      fromTokens,
      reason: 'the launcher background is '
          '#${fromResource.toRadixString(16).padLeft(6, '0')} but KhadraColors.price '
          'is #${fromTokens.toRadixString(16).padLeft(6, '0')} — change '
          '`_background` in tools/make_launcher_icons.dart and '
          '`adaptive_icon_background` in flutter_launcher_icons.yaml together',
    );
  });

  test('the launcher name is a resource, and Arabic has its own', () {
    // `android:label` was the literal "Khadra", which Android cannot translate: a
    // phone set to Arabic showed a Latin name beside an app that is otherwise
    // entirely in Arabic.
    final manifest =
        File('android/app/src/main/AndroidManifest.xml').readAsStringSync();
    expect(manifest, contains('android:label="@string/app_name"'));

    // Compared against the ARB rather than against a literal typed twice. The
    // stated invariant is "the launcher and the app agree about the app's name",
    // so that is the thing asserted: one is a Gradle resource and the other is a
    // generated Dart getter, and nothing but this notices when they part.
    expect(
      File('$res/values/strings.xml').readAsStringSync(),
      contains('<string name="app_name">${AppLocalizationsEn().appName}</string>'),
    );
    expect(
      File('$res/values-ar/strings.xml').readAsStringSync(),
      contains('<string name="app_name">${AppLocalizationsAr().appName}</string>'),
    );
  });
}
