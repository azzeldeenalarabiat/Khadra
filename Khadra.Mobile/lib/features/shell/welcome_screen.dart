import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/providers.dart';
import '../../core/router.dart';
import '../../core/theme/khadra_theme.dart';
import '../../core/widgets/khadra_widgets.dart';
import '../../l10n/app_localizations.dart';
import '../auth/auth_form_widgets.dart';

/// The app's front door, shown on a fresh install, after the app's data has been
/// cleared, and after a deliberate sign-out.
///
/// It exists because the app used to open on the catalogue, which says nothing
/// about whether there is an account, what one is for, or how to get one — so a
/// first-time customer discovered all three at the booking button. Three choices,
/// all of them visible, none of them dressed down to nudge somebody towards
/// another: browsing really is open, and a guest is not a second-class visitor.
///
/// **Nothing on it is invented.** No car count, no "from 15 JOD a day", no
/// promotion — the platform publishes no such figure, and a number typed into the
/// app's first screen would be the static-data rule broken on its front door. The
/// title, one sentence about what an account is for, and the three buttons are the
/// whole of it.
///
/// The design handoff draws no Get Started artboard, so this is built from
/// [AuthScaffold] and the token file rather than guessed at — it wears the same
/// frame as sign-in and registration, which is where two of its three buttons go.
class WelcomeScreen extends ConsumerWidget {
  const WelcomeScreen({super.key, this.next});

  /// Where the customer was heading when the app was launched.
  ///
  /// Only ever a route that needs an account: the router sends a destination to
  /// `/` solely when a GUARDED route was opened before the session had resolved.
  /// So it travels with the two buttons that can reach it and not with the third —
  /// offering to browse as a guest and then landing on somebody's own booking
  /// would only bounce them into the sign-in form they had just declined.
  final String? next;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);

    return AuthScaffold(
      title: l10n.welcomeTitle,
      subtitle: l10n.welcomeBody,
      children: [
        FilledButton(
          onPressed: () => _browse(context, ref),
          child: Text(l10n.welcomeBrowseAsGuest),
        ),
        const SizedBox(height: Space.md),
        OutlinedButton(
          // Pushed, not replaced, so the back gesture returns here rather than
          // leaving somebody on a form they did not choose with nowhere above it.
          onPressed: () => context.push(routeWithNext(Routes.signIn, next)),
          child: Text(l10n.authSignIn),
        ),
        const SizedBox(height: Space.sm),
        OutlinedButton(
          onPressed: () => context.push(routeWithNext(Routes.register, next)),
          child: Text(l10n.authSignUp),
        ),
        const SizedBox(height: Space.xxl),
        const _LanguageRow(),
      ],
    );
  }

  /// Records the choice, then opens the app.
  ///
  /// The flag is written here and NOT when the other two buttons are tapped:
  /// tapping Sign in and then backing out is not a choice, and a customer who did
  /// that would otherwise find this screen gone on the next launch having never
  /// decided anything. The two forms set it themselves once they succeed.
  Future<void> _browse(BuildContext context, WidgetRef ref) async {
    await ref.read(entryChoiceProvider.notifier).choose();
    if (context.mounted) context.go(Routes.search);
  }
}

/// The language switch, on the first screen anybody sees.
///
/// The app already follows the device, which is the right default — a phone set to
/// Arabic opens in Arabic without being asked. This is for the other case, which is
/// common in Jordan: an Arabic speaker whose phone is in English. Until now the
/// only switch was inside the Profile tab, which is a long way from the front door
/// for somebody who cannot read the screen they are standing on.
///
/// Two languages, each written in ITSELF, so the way out of the wrong one does not
/// require reading the wrong one. "Follow my device" is not offered here: it is
/// already in force until one of these is tapped, and it lives in Profile for
/// anybody who wants to go back to it.
class _LanguageRow extends ConsumerWidget {
  const _LanguageRow();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);
    final arabic = ref.watch(isArabicProvider);

    void choose(String code) =>
        ref.read(localeProvider.notifier).set(Locale(code));

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        KhadraFieldLabel(l10n.profileLanguage),
        Row(
          children: [
            KhadraChoiceChip(
              label: l10n.profileLanguageEnglish,
              selected: !arabic,
              onTap: () => choose('en'),
            ),
            const SizedBox(width: Space.sm),
            KhadraChoiceChip(
              label: l10n.profileLanguageArabic,
              selected: arabic,
              onTap: () => choose('ar'),
            ),
          ],
        ),
      ],
    );
  }
}
