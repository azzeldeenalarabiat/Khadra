import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../../core/router.dart';
import '../../core/theme/khadra_theme.dart';
import '../../core/widgets/khadra_widgets.dart';
import '../../l10n/app_localizations.dart';

/// What stands where a signed-out customer's own data would be.
///
/// One panel for all three account tabs, so they cannot drift. They had drifted:
/// Bookings and Alerts offered Sign in and no way to CREATE an account, and the
/// Alerts tab said "Sign in to see your bookings" — it was handed the bookings
/// string. A guest reading that had been told about the wrong screen and shown only
/// half the way in.
///
/// **Why a panel and not a redirect.** The account-only data is already behind
/// authentication in the two places that matter: the providers never call the API
/// without a session, and every one of those endpoints is refused server-side
/// regardless. Sending the tab itself to the sign-in form would leave the tab
/// shell, so the bottom bar disappears and the only way back is the form's close
/// button — and the Profile tab is where the language switch lives, so an Arabic
/// speaker who has not signed in would be locked out of the one control that gets
/// them out of English. A visible tab that says plainly what it needs is also how a
/// guest finds out there is an account worth having.
class AccountRequired extends StatelessWidget {
  const AccountRequired({
    super.key,
    required this.icon,
    required this.title,
    required this.next,
  });

  final IconData icon;

  /// Names THIS screen's own subject. Shared copy below it, a per-screen line
  /// above: somebody on Alerts is told about alerts.
  final String title;

  /// Where to come back to once they are signed in, so the tap that asked for an
  /// account is the tap that is answered.
  final String next;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);

    return KhadraEmpty(
      icon: icon,
      title: title,
      body: l10n.accountRequiredBody,
      action: ConstrainedBox(
        constraints: const BoxConstraints(maxWidth: 260),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            FilledButton(
              onPressed: () =>
                  context.push(routeWithNext(Routes.signIn, next)),
              child: Text(l10n.authSignIn),
            ),
            const SizedBox(height: Space.sm),
            OutlinedButton(
              // The destination goes to BOTH forms. It used to go only to sign-in,
              // so somebody who tapped Create account from the Bookings tab, made
              // an account and verified it landed on the catalogue rather than on
              // the bookings they had asked for.
              onPressed: () =>
                  context.push(routeWithNext(Routes.register, next)),
              child: Text(l10n.authSignUp),
            ),
          ],
        ),
      ),
    );
  }
}
