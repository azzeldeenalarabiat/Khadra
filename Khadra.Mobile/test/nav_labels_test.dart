import 'package:flutter_test/flutter_test.dart';
import 'package:khadra_mobile/l10n/app_localizations.dart';
import 'package:khadra_mobile/l10n/app_localizations_ar.dart';
import 'package:khadra_mobile/l10n/app_localizations_en.dart';

/// What the four bottom tabs are CALLED, in both languages.
///
/// These four strings are the most-read words in the app and the owner names
/// them, not the code: the first tab was "Browse" until 2026-09-11 and is "Home"
/// because that is the decision, not because it is a better word. A test that
/// states the decision is what makes a future edit to the ARB argue with
/// somebody rather than land quietly.
///
/// It pins the STRING, which is the whole of it: `NavigationDestination` takes
/// one `label` and uses it both as the visible text and as the semantics label a
/// screen reader announces, and `app_shell.dart` overrides neither `tooltip` nor
/// any `Semantics` wrapper. There is one name per tab, not two that could drift.
void main() {
  const tabs = <String, (String english, String arabic)>{
    'first': ('Home', 'الرئيسية'),
    'second': ('Bookings', 'حجوزاتي'),
    'third': ('Alerts', 'التنبيهات'),
    'fourth': ('Profile', 'حسابي'),
  };

  final en = AppLocalizationsEn();
  final ar = AppLocalizationsAr();

  List<String> namesIn(AppLocalizations l10n) =>
      <String>[l10n.navHome, l10n.navBookings, l10n.navNotifications, l10n.navProfile];

  test('the tabs are named what the owner named them', () {
    expect(namesIn(en), tabs.values.map((t) => t.$1).toList());
    expect(namesIn(ar), tabs.values.map((t) => t.$2).toList());
  });

  test('no tab is left in English on the Arabic side', () {
    // The parity test catches a MISSING translation; this catches one that was
    // added by copying the English across, which reads as translated until
    // somebody who speaks the language opens the app.
    for (final (index, arabic) in namesIn(ar).indexed) {
      expect(arabic, isNot(namesIn(en)[index]));
      expect(
        arabic.runes.any((r) => r >= 0x0600 && r <= 0x06FF),
        isTrue,
        reason: '"$arabic" has no Arabic letters in it',
      );
    }
  });
}
