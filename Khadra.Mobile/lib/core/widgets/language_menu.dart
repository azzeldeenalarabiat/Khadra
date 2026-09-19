import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../l10n/app_localizations.dart';
import '../providers.dart';
import '../theme/khadra_theme.dart';

/// The language switch for the screens somebody meets before they have an account:
/// Get Started, and every form in the sign-in and registration flow.
///
/// A globe rather than a word, because the person looking for this is the one who
/// cannot read the screen they are on, and each language in the menu is written in
/// ITSELF for the same reason. "Follow my device" is not offered: it is already in
/// force until one of these is chosen, and Profile keeps it for anybody who wants to
/// go back to it.
///
/// A choice is remembered at once (`khadra.locale`), and the app turns to the new
/// reading direction without a restart.
class KhadraLanguageMenu extends ConsumerWidget {
  const KhadraLanguageMenu({super.key, this.onBrand = false});

  /// Drawn on Get Started's green rather than on a white app bar.
  final bool onBrand;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);
    final current = ref.watch(isArabicProvider) ? 'ar' : 'en';

    return PopupMenuButton<String>(
      tooltip: l10n.profileLanguage,
      initialValue: current,
      onSelected: (code) => ref.read(localeProvider.notifier).set(Locale(code)),
      icon: const Icon(Icons.language),
      iconColor: onBrand ? KhadraColors.onBrand : KhadraColors.text,
      style: onBrand
          ? IconButton.styleFrom(backgroundColor: KhadraColors.onBrandFaint)
          : null,
      itemBuilder: (context) => [
        CheckedPopupMenuItem<String>(
          value: 'en',
          checked: current == 'en',
          child: Text(l10n.profileLanguageEnglish),
        ),
        CheckedPopupMenuItem<String>(
          value: 'ar',
          checked: current == 'ar',
          child: Text(l10n.profileLanguageArabic),
        ),
      ],
    );
  }
}
