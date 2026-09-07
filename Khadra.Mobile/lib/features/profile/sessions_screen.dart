import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/dtos.dart';
import '../../core/api/api_failure.dart';
import '../../core/api/api_failure_messages.dart';
import '../../core/format/booking_presentation.dart';
import '../../core/providers.dart';
import '../../core/theme/khadra_theme.dart';
import '../../core/widgets/khadra_widgets.dart';
import '../../l10n/app_localizations.dart';

final _sessionsProvider = FutureProvider.autoDispose<MySessions>(
  (ref) => ref.watch(apiProvider).sessions(),
);

/// Where this account is signed in.
///
/// One row per SIGN-IN, not per token refresh: the server groups by refresh-token
/// family, so a device that has rotated a hundred times is still one line.
class SessionsScreen extends ConsumerWidget {
  const SessionsScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);
    final sessions = ref.watch(_sessionsProvider);

    return Scaffold(
      appBar: AppBar(title: Text(l10n.profileSessions)),
      body: RefreshIndicator(
        onRefresh: () => ref.refresh(_sessionsProvider.future),
        child: switch (sessions) {
          AsyncLoading() => const KhadraLoading(),
          AsyncError(:final error) => KhadraError(
              message: ApiFailure.from(error).messageFor(l10n),
              onRetry: () => ref.invalidate(_sessionsProvider),
            ),
          AsyncData(:final value) when value.sessions.isEmpty => ListView(
              children: [
                SizedBox(
                  height: MediaQuery.of(context).size.height * 0.5,
                  child: KhadraEmpty(
                    icon: Icons.devices_outlined,
                    title: l10n.profileSessionsEmpty,
                  ),
                ),
              ],
            ),
          AsyncData(:final value) => ListView.separated(
              padding: const EdgeInsets.fromLTRB(
                  Space.lg, Space.lg, Space.lg, Space.bottomInset),
              itemCount: value.sessions.length,
              separatorBuilder: (_, __) => const SizedBox(height: Space.md),
              itemBuilder: (_, index) =>
                  _SessionCard(session: value.sessions[index]),
            ),
          _ => const KhadraLoading(),
        },
      ),
    );
  }
}

class _SessionCard extends ConsumerWidget {
  const _SessionCard({required this.session});

  final SessionSummary session;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);

    return KhadraCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Icon(
                _icon(session.userAgent),
                color: KhadraColors.neutral600,
              ),
              const SizedBox(width: Space.md),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      _describe(session.userAgent),
                      style: const TextStyle(
                          fontSize: 15, fontWeight: FontWeight.w600),
                    ),
                    const SizedBox(height: 2),
                    Text(
                      l10n.profileSessionLastUsed(
                        BookingPresentation.relative(l10n, session.lastUsedAt),
                      ),
                      style: const TextStyle(
                          color: KhadraColors.neutral600, fontSize: 12),
                    ),
                    if (session.createdByIp != null) ...[
                      const SizedBox(height: 2),
                      LatinRun(
                        session.createdByIp!,
                        style: const TextStyle(
                            color: KhadraColors.neutral500, fontSize: 11),
                      ),
                    ],
                  ],
                ),
              ),
            ],
          ),
          if (session.isActive) ...[
            const SizedBox(height: Space.md),
            SizedBox(
              width: double.infinity,
              child: OutlinedButton.icon(
                onPressed: () => _revoke(context, ref),
                icon: const Icon(Icons.logout, size: 18),
                label: Text(l10n.profileSessionRevoke),
                style: OutlinedButton.styleFrom(
                  foregroundColor: KhadraColors.bad,
                  side: const BorderSide(color: KhadraColors.bad),
                ),
              ),
            ),
          ],
        ],
      ),
    );
  }

  Future<void> _revoke(BuildContext context, WidgetRef ref) async {
    final l10n = AppLocalizations.of(context);
    try {
      await ref.read(apiProvider).revokeSession(session.familyId);
      ref.invalidate(_sessionsProvider);
      if (context.mounted) {
        showKhadraMessage(context, l10n.profileSessionRevoked);
      }
      // Revoking THIS device's own family is allowed and is a legitimate thing to
      // want. The next request then fails, the interceptor's refresh is refused,
      // and the session ends -- which is exactly the outcome that was asked for,
      // so nothing here needs to special-case it.
    } on ApiFailure catch (failure) {
      if (context.mounted) {
        showKhadraMessage(context, failure.messageFor(l10n), isError: true);
      }
    }
  }

  /// A readable device name from a user agent string.
  ///
  /// Coarse on purpose: the point is "was that me?", and a full UA string is
  /// unreadable on a phone.
  static String _describe(String? userAgent) {
    if (userAgent == null || userAgent.isEmpty) return '—';
    final agent = userAgent.toLowerCase();
    if (agent.contains('android')) return 'Android';
    if (agent.contains('iphone') || agent.contains('ios')) return 'iPhone';
    if (agent.contains('ipad')) return 'iPad';
    if (agent.contains('dart') || agent.contains('okhttp')) return 'Khadra';
    if (agent.contains('windows')) return 'Windows';
    if (agent.contains('mac')) return 'Mac';
    return userAgent.length > 40 ? '${userAgent.substring(0, 40)}…' : userAgent;
  }

  static IconData _icon(String? userAgent) {
    final agent = (userAgent ?? '').toLowerCase();
    if (agent.contains('android') ||
        agent.contains('iphone') ||
        agent.contains('dart')) {
      return Icons.smartphone_outlined;
    }
    if (agent.contains('ipad')) return Icons.tablet_outlined;
    return Icons.computer_outlined;
  }
}
