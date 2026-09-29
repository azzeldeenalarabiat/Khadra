import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/providers.dart';
import '../../l10n/app_localizations.dart';

/// The app's licences (pre-launch item 201; owner, 2026-09-29), opened from About on the Profile tab.
///
/// Flutter's own licence page, deliberately: it lists every open-source package this build bundles —
/// collected by the build itself, so nothing here can fall behind the dependencies — beside the fonts'
/// licences the app adds to the same registry at start (`registerFontLicences`), and opens each one's
/// full text. No account is needed to read it.
///
/// The page words its own title and counts in Material's strings, whose default English spells them
/// "Licenses"; this app writes British English, and the row that opens the page says "Licences", so in
/// English the page reads Material's British strings. Arabic is «التراخيص» either way.
class LicencesScreen extends ConsumerWidget {
  const LicencesScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final page = LicensePage(
      applicationName: AppLocalizations.of(context).appName,
      // The version the platform reports for this install, or none when it would not say.
      applicationVersion: ref.watch(installedAppVersionProvider),
    );

    return Localizations.localeOf(context).languageCode == 'en'
        ? Localizations.override(context: context, locale: const Locale('en', 'GB'), child: page)
        : page;
  }
}
