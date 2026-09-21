import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../l10n/app_localizations.dart';
import '../providers.dart';
import '../theme/khadra_theme.dart';
import 'khadra_widgets.dart';

/// Says, on every screen that can take money, that this build takes none.
///
/// A sandbox capture writes the same `Confirmed` status, the same row and the
/// same screen as a real one. That is what makes it useful for testing and what
/// makes it dangerous afterwards: within a day nobody can tell which bookings had
/// money behind them. The record carries a permanent marker for whoever reads the
/// database later; this is the half for whoever is holding the phone now.
///
/// **It renders nothing unless the server said `Sandbox`.** Not when the config
/// has not arrived, not on an older server, and not on a value this release has
/// never seen. The asymmetry is deliberate: a banner missed on a test host is a
/// nuisance, and a banner shown to a paying customer tells them their payment was
/// fake. Production can never report Sandbox — the API refuses to start on that
/// provider — so the banner is only ever news where the news is true.
class SandboxPaymentsBanner extends ConsumerWidget {
  const SandboxPaymentsBanner({super.key, this.padding});

  /// Where the screen wants it. Null means flush, for a screen with its own gutters.
  final EdgeInsetsGeometry? padding;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    if (!ref.watch(sandboxPaymentsProvider)) return const SizedBox.shrink();

    final l10n = AppLocalizations.of(context);
    final notice = KhadraNotice(
      title: l10n.sandboxPaymentsTitle,
      body: l10n.sandboxPaymentsBody,
      tone: NoticeTone.warn,
      icon: Icons.science_outlined,
    );

    return padding == null ? notice : Padding(padding: padding!, child: notice);
  }
}

/// The same fact about ONE attempt, from the attempt's own marker.
///
/// Separate from the banner above because they answer different questions. That
/// one is about this deployment now; this is about the record, which keeps its
/// answer for as long as the row exists. A booking paid on a sandbox host stays a
/// sandbox booking when it is read on a host configured any other way — which is
/// the entire point of the marker living on the row rather than in a setting.
class SandboxAttemptNote extends StatelessWidget {
  const SandboxAttemptNote({super.key, required this.isSandbox});

  final bool isSandbox;

  @override
  Widget build(BuildContext context) {
    if (!isSandbox) return const SizedBox.shrink();

    return Text(
      AppLocalizations.of(context).sandboxPaymentsAttempt,
      style: const TextStyle(
        fontSize: 13,
        height: 1.4,
        fontWeight: FontWeight.w600,
        color: KhadraColors.warn,
      ),
    );
  }
}
