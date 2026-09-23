import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../l10n/app_localizations.dart';
import '../config/app_environment.dart';
import '../providers.dart';
import '../theme/khadra_theme.dart';

/// A strip across the top of EVERY screen of the staging build, saying it is not the real app.
///
/// Mounted once, in `MaterialApp.builder`, above the router and above the update screen, so no
/// route, dialog-free page or deep link can render without it. It takes the status-bar inset itself
/// and hands the screen beneath a zero top padding, so nothing is drawn behind it.
///
/// Two facts, from two sources, and deliberately not merged:
///
/// - **"Test build · staging server"** is a property of THIS BINARY, from its flavor. It is shown
///   from the first frame, before the network has answered anything.
/// - **"Sandbox payments"** is the SERVER's `payments.mode`, and appears only once `/app-config` has
///   said Sandbox. The flavor is not evidence of what the server does with money, so it never claims
///   it; the in-screen `SandboxPaymentsBanner` keys on the same server value.
///
/// Renders the child untouched in production, so the customer app is not one pixel different.
class EnvironmentRibbon extends ConsumerWidget {
  const EnvironmentRibbon({super.key, required this.child, this.show});

  final Widget child;

  /// Whether to draw it. Null means "this build is staging", which is what the app passes; tests
  /// set it, because a test process has no flavor to be.
  final bool? show;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    if (!(show ?? AppEnvironment.isStaging)) return child;

    final l10n = AppLocalizations.of(context);
    final sandbox = ref.watch(sandboxPaymentsProvider);
    final media = MediaQuery.of(context);
    final label = sandbox
        ? '${l10n.environmentStagingRibbon} · ${l10n.environmentSandboxPayments}'
        : l10n.environmentStagingRibbon;

    return Column(
      children: [
        Semantics(
          container: true,
          label: label,
          child: ExcludeSemantics(
            child: Material(
              // A Material rather than a ColoredBox: this sits above every Scaffold, and Text
              // outside a Material has no default style to inherit.
              key: const ValueKey('environment-ribbon'),
              color: KhadraColors.warn,
              child: Padding(
                padding: EdgeInsetsDirectional.only(
                  top: media.padding.top,
                  start: Space.lg,
                  end: Space.lg,
                ),
                child: SizedBox(
                  width: double.infinity,
                  height: 22,
                  child: Row(
                    mainAxisAlignment: MainAxisAlignment.center,
                    children: [
                      const Icon(Icons.science_outlined, size: 14, color: KhadraColors.onBrand),
                      const SizedBox(width: Space.xs),
                      Flexible(
                        child: Text(
                          label,
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                          style: const TextStyle(
                            color: KhadraColors.onBrand,
                            fontSize: 12,
                            fontWeight: FontWeight.w700,
                          ),
                        ),
                      ),
                    ],
                  ),
                ),
              ),
            ),
          ),
        ),
        Expanded(
          child: MediaQuery.removePadding(
            context: context,
            removeTop: true,
            child: child,
          ),
        ),
      ],
    );
  }
}
