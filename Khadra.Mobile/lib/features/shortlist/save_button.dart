import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/api/api_failure.dart';
import '../../core/api/api_failure_messages.dart';
import '../../core/providers.dart';
import '../../core/router.dart';
import '../../core/theme/khadra_theme.dart';
import '../../core/widgets/khadra_widgets.dart';
import '../../l10n/app_localizations.dart';
import 'shortlist_providers.dart';

/// The heart.
///
/// **Signed out it is not hidden and not disabled — it leads to sign-in**, with
/// the car as its destination, so somebody comes back to the page they were
/// looking at. A control that vanishes for half the audience makes the feature
/// undiscoverable; one that is disabled is an affordance that does nothing.
///
/// A refusal from the server puts the heart straight back and says why. The full
/// list is the refusal worth showing: it carries the platform's own figure, so
/// the customer is told how many they may keep rather than that "something went
/// wrong".
class SaveButton extends ConsumerWidget {
  const SaveButton({
    super.key,
    required this.vehicleId,
    this.size = 20,
    this.onSurface = false,
  });

  final String vehicleId;
  final double size;

  /// Sitting over a photograph rather than on a card, so it needs its own
  /// backing to stay legible against whatever the picture happens to be.
  final bool onSurface;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);
    final signedIn = ref.watch(sessionProvider).isSignedIn;
    final saved = signedIn && ref.watch(isSavedProvider(vehicleId));

    final icon = Icon(
      saved ? Icons.favorite : Icons.favorite_border,
      size: size,
      color: saved ? KhadraColors.badStrong : KhadraColors.neutral600,
    );

    final button = IconButton(
      onPressed: () => _tap(context, ref, signedIn),
      icon: icon,
      tooltip: saved ? l10n.shortlistRemove : l10n.shortlistSave,
      visualDensity: VisualDensity.compact,
      padding: const EdgeInsets.all(Space.sm),
      constraints: const BoxConstraints(minWidth: 40, minHeight: 40),
    );

    if (!onSurface) return button;

    return DecoratedBox(
      decoration: BoxDecoration(
        color: KhadraColors.surface.withValues(alpha: 0.92),
        shape: BoxShape.circle,
      ),
      child: button,
    );
  }

  Future<void> _tap(BuildContext context, WidgetRef ref, bool signedIn) async {
    final l10n = AppLocalizations.of(context);

    if (!signedIn) {
      // The destination travels, so signing in lands back on the car rather than
      // dumping somebody on the search screen having lost what they were reading.
      context.push(
        Uri(
          path: Routes.signIn,
          queryParameters: {'next': Routes.vehicle(vehicleId)},
        ).toString(),
      );
      return;
    }

    final failure = await ref.read(savedVehiclesProvider.notifier).toggle(vehicleId);
    if (failure == null || !context.mounted) return;

    showKhadraMessage(
      context,
      ApiFailure.from(failure).messageFor(l10n),
      isError: true,
    );
  }
}
