import 'package:flutter/material.dart';
import 'package:url_launcher/url_launcher.dart';

import '../../core/config/update_requirement.dart';
import '../../core/theme/khadra_theme.dart';
import '../../core/widgets/language_menu.dart';
import '../../l10n/app_localizations.dart';

/// The whole app, while this build is too old to use.
///
/// Put up by the app's root in place of the router (`main.dart`), so no route and
/// no deep link can reach past it: there is nothing behind it to navigate to. It
/// is not dismissible, and Back closes the app, because every screen it could go
/// back to is one whose contract this build may no longer read.
///
/// Everything it says is the platform's or the server's: the installed version
/// from the phone, the minimum and the download link from the API. When the API
/// has published no link it says where to look instead of inventing one.
///
/// The language menu is here for the same reason it is on Get Started — the
/// person who most needs this screen may be the one who cannot read it — and the
/// stored sign-in is untouched throughout, so the new build opens signed in.
class UpdateRequiredScreen extends StatelessWidget {
  const UpdateRequiredScreen({super.key, required this.requirement});

  final UpdateRequirement requirement;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final text = Theme.of(context).textTheme;
    final installed = requirement.installed;
    final minimum = requirement.minimum;
    final link = requirement.updateUrl;

    return Scaffold(
      backgroundColor: KhadraColors.background,
      appBar: AppBar(
        automaticallyImplyLeading: false,
        backgroundColor: KhadraColors.background,
        actions: const [KhadraLanguageMenu()],
      ),
      body: SafeArea(
        child: Center(
          child: SingleChildScrollView(
            child: ConstrainedBox(
              constraints: const BoxConstraints(maxWidth: Space.measure),
              child: Padding(
                padding: const EdgeInsets.all(Space.xl),
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    const Icon(Icons.system_update_outlined, size: 56, color: KhadraColors.accent),
                    const SizedBox(height: Space.lg),
                    Text(
                      l10n.updateRequiredTitle,
                      style: text.headlineSmall,
                      textAlign: TextAlign.center,
                    ),
                    const SizedBox(height: Space.sm),
                    Text(
                      l10n.updateRequiredBody,
                      style: text.bodyMedium?.copyWith(color: KhadraColors.neutral600),
                      textAlign: TextAlign.center,
                    ),
                    if (installed != null && minimum != null) ...[
                      const SizedBox(height: Space.md),
                      Text(
                        l10n.updateRequiredVersions(installed.toString(), minimum.toString()),
                        style: text.bodySmall?.copyWith(color: KhadraColors.neutral600),
                        textAlign: TextAlign.center,
                      ),
                    ],
                    const SizedBox(height: Space.xl),
                    if (link != null)
                      FilledButton(
                        onPressed: () => launchUrl(link, mode: LaunchMode.externalApplication),
                        child: Text(l10n.updateRequiredAction),
                      )
                    else
                      Text(
                        l10n.updateRequiredWhereFrom,
                        style: text.bodyMedium,
                        textAlign: TextAlign.center,
                      ),
                  ],
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}
