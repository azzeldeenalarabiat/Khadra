import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/dtos.dart';
import '../../core/api/api_failure.dart';
import '../../core/api/api_failure_messages.dart';
import '../../core/format/booking_presentation.dart';
import '../../core/providers.dart';
import '../../core/router.dart';
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
      appBar: AppBar(
        leading: const KhadraBack(fallback: Routes.profile),
        title: Text(l10n.profileSessions),
      ),
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
                    Row(
                      children: [
                        Flexible(
                          child: Text(
                            _describe(session.userAgent, l10n),
                            style: const TextStyle(
                                fontSize: 15, fontWeight: FontWeight.w600),
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                          ),
                        ),
                        // Which row is the phone being held. The server answers it — see
                        // SessionSummary.isCurrent — and an unmarked list is the honest
                        // state of a build that cannot say, not a reason to guess.
                        if (session.isCurrent) ...[
                          const SizedBox(width: Space.sm),
                          Container(
                            padding: const EdgeInsets.symmetric(
                                horizontal: Space.sm, vertical: 2),
                            decoration: BoxDecoration(
                              color: KhadraColors.accent.withValues(alpha: 0.12),
                              borderRadius: Radii.pill,
                            ),
                            child: Text(
                              l10n.profileSessionThis,
                              style: const TextStyle(
                                fontSize: 11,
                                fontWeight: FontWeight.w700,
                                color: KhadraColors.accent,
                              ),
                            ),
                          ),
                        ],
                      ],
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

  /// The app's own stamp: `Khadra (Android 16)`. What is in the brackets is the
  /// operating system and its version, which is the part worth reading.
  static final RegExp _ourStamp = RegExp(r'^Khadra \((.+)\)$');

  /// `ios` as a WORD. As a bare substring it also matches `axios/1.x`, which is
  /// a script somebody ran and not an iPhone.
  static final RegExp _iosToken = RegExp(r'(?<![a-z])ios(?![a-z])');

  /// A readable device name from a user agent string.
  ///
  /// Coarse on purpose: the point is "was that me?", and a full UA string is
  /// unreadable on a phone.
  ///
  /// It used to answer "Khadra" for anything sent by this app, because the app
  /// set no `User-Agent` and the HTTP client's default — `Dart/3.x (dart:io)` —
  /// was matched on the word "dart". A customer opening Registered Devices read
  /// the app's own name where the device should be, on every row, which says
  /// nothing about which phone it is and reads as though Khadra were a device.
  /// The app now stamps the operating system; a session from a build that did
  /// not is named as what it is, which is unrecognised.
  static String _describe(String? userAgent, AppLocalizations l10n) {
    final raw = userAgent?.trim() ?? '';
    if (raw.isEmpty) return l10n.profileSessionUnknownDevice;

    final ours = _ourStamp.firstMatch(raw);
    if (ours != null) return ours.group(1)!.trim();

    final agent = raw.toLowerCase();
    if (agent.contains('ipad')) return 'iPad';
    if (agent.contains('iphone') || _iosToken.hasMatch(agent)) return 'iPhone';
    if (agent.contains('android')) return 'Android';
    if (agent.contains('windows')) return 'Windows';
    if (agent.contains('mac')) return 'Mac';
    if (agent.contains('linux')) return 'Linux';
    if (agent.contains('dart') || agent.contains('okhttp')) {
      return l10n.profileSessionUnknownDevice;
    }
    return raw.length > 40 ? '${raw.substring(0, 40)}…' : raw;
  }

  static IconData _icon(String? userAgent) {
    final agent = (userAgent ?? '').toLowerCase();
    if (agent.contains('ipad')) return Icons.tablet_outlined;
    if (agent.contains('android') ||
        agent.contains('iphone') ||
        _iosToken.hasMatch(agent) ||
        agent.contains('dart') ||
        agent.contains('okhttp')) {
      return Icons.smartphone_outlined;
    }
    return Icons.computer_outlined;
  }
}
