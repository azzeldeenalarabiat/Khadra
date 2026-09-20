import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:khadra_mobile/core/providers.dart';
import 'package:khadra_mobile/core/theme/khadra_theme.dart';
import 'package:khadra_mobile/core/widgets/khadra_widgets.dart';
import 'package:khadra_mobile/features/shell/welcome_screen.dart';
import 'package:khadra_mobile/l10n/app_localizations.dart';
import 'package:shared_preferences/shared_preferences.dart';

/// Get Started as it LOOKS, on the phones and at the text sizes it has to survive.
///
/// `entry_gate_test.dart` holds what each button does. This holds whether the three
/// of them can be reached and read at all — on a 360x640 phone with the system text
/// turned up, in Arabic — and whether the language switch actually turns the screen
/// around. A pair of fixed halves is exactly what cropped the Arabic filter label
/// before.
void main() {
  setUpAll(() async {
    TestWidgetsFlutterBinding.ensureInitialized();

    // The real faces, so every width measured here is the width a phone will
    // draw. Noto Kufi Arabic's deeper line box is half the point.
    for (final family in <String, List<String>>{
      'Manrope': [
        'Manrope-400.ttf',
        'Manrope-500.ttf',
        'Manrope-600.ttf',
        'Manrope-700.ttf',
        'Manrope-800.ttf',
      ],
      'Noto Kufi Arabic': [
        'NotoKufiArabic-400.ttf',
        'NotoKufiArabic-500.ttf',
        'NotoKufiArabic-600.ttf',
        'NotoKufiArabic-700.ttf',
      ],
    }.entries) {
      final loader = FontLoader(family.key);
      for (final file in family.value) {
        loader.addFont(Future<ByteData>.value(
            ByteData.sublistView(File('assets/fonts/$file').readAsBytesSync())));
      }
      await loader.load();
    }
  });

  /// Get Started in an app whose language follows the stored choice, the way
  /// `main.dart` wires it, so choosing a language turns the screen around here too.
  Future<SharedPreferences> pumpWelcome(
    WidgetTester tester, {
    required Size size,
    required Locale locale,
    double textScale = 1,
  }) async {
    tester.view.physicalSize = size;
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);

    SharedPreferences.setMockInitialValues({'khadra.locale': locale.languageCode});
    final preferences = await SharedPreferences.getInstance();

    await tester.pumpWidget(
      ProviderScope(
        overrides: [sharedPreferencesProvider.overrideWithValue(preferences)],
        child: Consumer(
          builder: (context, ref, _) => MaterialApp(
            locale: ref.watch(localeProvider),
            theme: KhadraTheme.light(),
            supportedLocales: AppLocalizations.supportedLocales,
            localizationsDelegates: const [
              AppLocalizations.delegate,
              GlobalMaterialLocalizations.delegate,
              GlobalWidgetsLocalizations.delegate,
              GlobalCupertinoLocalizations.delegate,
            ],
            builder: (context, child) => MediaQuery(
              data: MediaQuery.of(context)
                  .copyWith(textScaler: TextScaler.linear(textScale)),
              child: child ?? const SizedBox.shrink(),
            ),
            home: const WelcomeScreen(),
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();

    return preferences;
  }

  /// Whether the label inside [button] was set on ONE line.
  ///
  /// Measured against the same words, in the same style at the same text size, laid
  /// out with nowhere to wrap: a label that broke onto a second line is taller.
  bool singleLine(WidgetTester tester, Finder button, String label) {
    final text = find.descendant(of: button, matching: find.text(label));
    final context = tester.element(text);
    final painter = TextPainter(
      text: TextSpan(text: label, style: DefaultTextStyle.of(context).style),
      textDirection: Directionality.of(context),
      textScaler: MediaQuery.textScalerOf(context),
      maxLines: 1,
    )..layout();
    final oneLine = painter.height;
    painter.dispose();

    return tester.getSize(text).height <= oneLine + 0.5;
  }

  const phones = [Size(360, 640), Size(412, 915)];
  const locales = [Locale('en'), Locale('ar')];
  const textScales = [1.0, 1.4];

  for (final phone in phones) {
    for (final locale in locales) {
      for (final scale in textScales) {
        final name = '${phone.width.toInt()}x${phone.height.toInt()}, '
            '${locale.languageCode}, text x$scale';

        testWidgets('every way in can be reached and read on $name',
            (tester) async {
          final semantics = tester.ensureSemantics();
          await pumpWelcome(tester, size: phone, locale: locale, textScale: scale);
          final l10n = lookupAppLocalizations(locale);

          // An overflow is reported as an exception rather than failing a test.
          expect(tester.takeException(), isNull);

          final buttons = {
            find.widgetWithText(FilledButton, l10n.welcomeBrowseAsGuest):
                l10n.welcomeBrowseAsGuest,
            find.widgetWithText(OutlinedButton, l10n.authSignIn): l10n.authSignIn,
            find.widgetWithText(OutlinedButton, l10n.authSignUp): l10n.authSignUp,
          };

          for (final MapEntry(key: button, value: label) in buttons.entries) {
            await tester.ensureVisible(button);
            await tester.pumpAndSettle();

            expect(button.hitTestable(), findsOneWidget,
                reason: '"$label" cannot be tapped');
            expect(singleLine(tester, button, label), isTrue,
                reason: '"$label" broke onto a second line');
          }

          // The globe as well as the buttons: every control is at least a finger
          // wide, and every one says what it is to a screen reader.
          await tester.ensureVisible(find.byTooltip(l10n.profileLanguage));
          await tester.pumpAndSettle();
          await expectLater(tester, meetsGuideline(androidTapTargetGuideline));
          await expectLater(tester, meetsGuideline(labeledTapTargetGuideline));

          semantics.dispose();
        });
      }
    }
  }

  testWidgets('the two account buttons share a row where both labels fit',
      (tester) async {
    await pumpWelcome(tester, size: const Size(480, 900), locale: const Locale('en'));
    final l10n = lookupAppLocalizations(const Locale('en'));

    expect(
      tester.getTopLeft(find.widgetWithText(OutlinedButton, l10n.authSignIn)).dy,
      tester.getTopLeft(find.widgetWithText(OutlinedButton, l10n.authSignUp)).dy,
    );
  });

  testWidgets('and stack where they do not, instead of cropping', (tester) async {
    await pumpWelcome(tester,
        size: const Size(360, 640), locale: const Locale('en'), textScale: 1.4);
    final l10n = lookupAppLocalizations(const Locale('en'));

    expect(
      tester.getTopLeft(find.widgetWithText(OutlinedButton, l10n.authSignUp)).dy,
      greaterThan(
          tester.getTopLeft(find.widgetWithText(OutlinedButton, l10n.authSignIn)).dy),
    );
  });

  testWidgets('a short phone gets a smaller badge, so the choices stay in view',
      (tester) async {
    await pumpWelcome(tester, size: const Size(360, 600), locale: const Locale('en'));
    final small = tester.widget<KhadraLogo>(find.byType(KhadraLogo)).size;

    await pumpWelcome(tester, size: const Size(412, 915), locale: const Locale('en'));
    final large = tester.widget<KhadraLogo>(find.byType(KhadraLogo)).size;

    expect(small, lessThan(large));
  });

  for (final locale in locales) {
    testWidgets('the globe sits at the reading END in ${locale.languageCode}',
        (tester) async {
      const phone = Size(412, 915);
      await pumpWelcome(tester, size: phone, locale: locale);
      final l10n = lookupAppLocalizations(locale);

      final x = tester.getCenter(find.byTooltip(l10n.profileLanguage)).dx;
      if (locale.languageCode == 'ar') {
        expect(x, lessThan(phone.width / 2));
      } else {
        expect(x, greaterThan(phone.width / 2));
      }
    });

    testWidgets('nothing on it is a figure the platform did not send, '
        'in ${locale.languageCode}', (tester) async {
      // No car count, no price, no rating. The screen has no API behind it, so a
      // digit anywhere on it could only have been typed into the app.
      await pumpWelcome(tester, size: const Size(412, 915), locale: locale);

      final digits = RegExp('[0-9٠-٩]');
      final texts = tester
          .widgetList<Text>(find.byType(Text))
          .map((text) => text.data ?? text.textSpan?.toPlainText() ?? '');
      expect(texts.where(digits.hasMatch), isEmpty);
    });
  }

  testWidgets(
      'the language menu names each language in itself, marks the one in use, '
      'and turns the screen around', (tester) async {
    const phone = Size(412, 915);
    final preferences =
        await pumpWelcome(tester, size: phone, locale: const Locale('en'));

    await tester.tap(find.byTooltip('Language'));
    await tester.pumpAndSettle();

    bool checked(String label) => tester
        .widget<CheckedPopupMenuItem<String>>(find.ancestor(
          of: find.text(label),
          matching: find.byType(CheckedPopupMenuItem<String>),
        ))
        .checked;

    expect(checked('English'), isTrue);
    expect(checked('العربية'), isFalse);

    await tester.tap(find.text('العربية'), warnIfMissed: false);
    await tester.pumpAndSettle();

    // Remembered on the device...
    expect(preferences.getString('khadra.locale'), 'ar');
    // ...laid out right to left rather than merely translated...
    expect(
      Directionality.of(tester.element(find.byType(WelcomeScreen))),
      TextDirection.rtl,
    );
    expect(find.text(lookupAppLocalizations(const Locale('ar')).welcomeTitle),
        findsOneWidget);
    // ...and the globe itself has moved to the new reading end.
    expect(tester.getCenter(find.byTooltip('اللغة')).dx, lessThan(phone.width / 2));
  });
}
