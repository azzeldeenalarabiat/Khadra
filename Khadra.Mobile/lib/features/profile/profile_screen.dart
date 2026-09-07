import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/providers.dart';
import '../../core/router.dart';
import '../../core/theme/khadra_theme.dart';
import '../../core/widgets/khadra_widgets.dart';
import '../../l10n/app_localizations.dart';
import '../documents/document_providers.dart';

class ProfileScreen extends ConsumerWidget {
  const ProfileScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);
    final session = ref.watch(sessionProvider);
    final formats = ref.watch(formatsProvider);

    return Scaffold(
      appBar: AppBar(title: Text(l10n.profileTitle)),
      body: ListView(
        padding: const EdgeInsets.only(bottom: Space.bottomInset),
        children: [
          if (session.isSignedIn) ...[
            _AccountHeader(
              name: session.user!.fullName,
              email: session.user!.email,
              memberSince: formats == null
                  ? null
                  : l10n.profileMemberSince(
                      formats.longDate(session.user!.createdAt)),
              verified: session.user!.isEmailVerified,
            ),
            if (!session.user!.isEmailVerified)
              Padding(
                padding: const EdgeInsets.fromLTRB(
                    Space.lg, 0, Space.lg, Space.lg),
                child: KhadraNotice(
                  title: l10n.profileEmailUnverified,
                  body: l10n.authVerifyEmailWhy,
                  tone: NoticeTone.warn,
                  icon: Icons.mark_email_unread_outlined,
                  action: OutlinedButton(
                    onPressed: () => context.push(
                      Uri(
                        path: Routes.verifyEmail,
                        queryParameters: {'email': session.user!.email},
                      ).toString(),
                    ),
                    child: Text(l10n.authResendVerification),
                  ),
                ),
              ),
            _Group(
              title: l10n.profilePersonalDetails,
              children: [
                _Row(
                  icon: Icons.person_outline,
                  label: l10n.profileEdit,
                  onTap: () => context.push(Routes.editProfile),
                ),
                _DocumentsRow(),
              ],
            ),
            _Group(
              title: l10n.profileSecurity,
              children: [
                _Row(
                  icon: Icons.lock_outline,
                  label: l10n.authChangePassword,
                  onTap: () => context.push(Routes.changePassword),
                ),
                _Row(
                  icon: Icons.devices_outlined,
                  label: l10n.profileSessions,
                  onTap: () => context.push(Routes.sessions),
                ),
              ],
            ),
          ] else
            Padding(
              padding: const EdgeInsets.all(Space.lg),
              child: KhadraCard(
                child: Column(
                  children: [
                    const KhadraWordmark(logoSize: 64),
                    const SizedBox(height: Space.lg),
                    Text(
                      l10n.bookingsSignedOutBody,
                      textAlign: TextAlign.center,
                      style: const TextStyle(
                          color: KhadraColors.neutral600,
                          fontSize: 14,
                          height: 1.5),
                    ),
                    const SizedBox(height: Space.lg),
                    FilledButton(
                      onPressed: () => context.push(Routes.signIn),
                      child: Text(l10n.authSignIn),
                    ),
                    const SizedBox(height: Space.sm),
                    OutlinedButton(
                      onPressed: () => context.push(Routes.register),
                      child: Text(l10n.authSignUp),
                    ),
                  ],
                ),
              ),
            ),

          const _LanguageGroup(),

          _Group(
            title: l10n.profileAbout,
            children: [
              Padding(
                padding: const EdgeInsets.all(Space.lg),
                child: Text(
                  l10n.profileAboutBody,
                  style: const TextStyle(
                      fontSize: 14, height: 1.55, color: KhadraColors.neutral700),
                ),
              ),
            ],
          ),

          if (session.isSignedIn) ...[
            const SizedBox(height: Space.sm),
            Padding(
              padding: const EdgeInsets.symmetric(horizontal: Space.lg),
              child: Column(
                children: [
                  OutlinedButton.icon(
                    onPressed: () => _signOut(context, ref, allDevices: false),
                    icon: const Icon(Icons.logout, size: 18),
                    label: Text(l10n.authSignOut),
                    style: OutlinedButton.styleFrom(
                      foregroundColor: KhadraColors.bad,
                      side: const BorderSide(color: KhadraColors.bad),
                    ),
                  ),
                  const SizedBox(height: Space.sm),
                  TextButton(
                    onPressed: () => _signOut(context, ref, allDevices: true),
                    child: Text(l10n.authSignOutEverywhere),
                  ),
                ],
              ),
            ),
          ],
        ],
      ),
    );
  }

  Future<void> _signOut(
    BuildContext context,
    WidgetRef ref, {
    required bool allDevices,
  }) async {
    final l10n = AppLocalizations.of(context);
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (_) => AlertDialog(
        title: Text(allDevices
            ? l10n.profileSignOutEverywhereConfirm
            : l10n.profileSignOutConfirm),
        actions: [
          TextButton(
            onPressed: () => Navigator.of(context).pop(false),
            child: Text(l10n.actionCancel),
          ),
          FilledButton(
            onPressed: () => Navigator.of(context).pop(true),
            child: Text(l10n.authSignOut),
          ),
        ],
      ),
    );

    if (confirmed != true) return;
    await ref.read(sessionProvider.notifier).signOut(allDevices: allDevices);
    if (context.mounted) context.go(Routes.search);
  }
}

class _AccountHeader extends StatelessWidget {
  const _AccountHeader({
    required this.name,
    required this.email,
    required this.memberSince,
    required this.verified,
  });

  final String name;
  final String email;
  final String? memberSince;
  final bool verified;

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.all(Space.lg),
        child: KhadraCard(
          child: Row(
            children: [
              CircleAvatar(
                radius: 26,
                backgroundColor: KhadraColors.accent100,
                child: Text(
                  _initials(name),
                  style: const TextStyle(
                    color: KhadraColors.accent,
                    fontWeight: FontWeight.w700,
                    fontSize: 18,
                  ),
                ),
              ),
              const SizedBox(width: Space.md),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      name,
                      style: const TextStyle(
                          fontSize: 17, fontWeight: FontWeight.w700),
                    ),
                    const SizedBox(height: 2),
                    // Latin inside an Arabic layout: isolated so the address does
                    // not have its parts reordered around it.
                    LatinRun(
                      email,
                      style: const TextStyle(
                          color: KhadraColors.neutral600, fontSize: 13),
                    ),
                    if (memberSince != null) ...[
                      const SizedBox(height: 2),
                      Text(
                        memberSince!,
                        style: const TextStyle(
                            color: KhadraColors.neutral500, fontSize: 12),
                      ),
                    ],
                  ],
                ),
              ),
              if (verified)
                const Icon(Icons.verified_outlined,
                    color: KhadraColors.accent, size: 20),
            ],
          ),
        ),
      );

  static String _initials(String name) {
    final parts = name.trim().split(RegExp(r'\s+'));
    if (parts.isEmpty || parts.first.isEmpty) return '?';
    if (parts.length == 1) return parts.first.characters.first.toUpperCase();
    return (parts.first.characters.first + parts.last.characters.first)
        .toUpperCase();
  }
}

/// The documents row, carrying the checklist's own verdict.
///
/// Read from the server rather than guessed: the badge says what the platform
/// thinks, which is what the booking button will be judged against.
class _DocumentsRow extends ConsumerWidget {
  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);
    final documents = ref.watch(myDocumentsProvider).valueOrNull;

    return _Row(
      icon: Icons.badge_outlined,
      label: l10n.documentsTitle,
      trailing: documents == null
          ? null
          : KhadraBadge(
              label: documents.isComplete
                  ? l10n.documentsComplete
                  : l10n.documentsMissing,
              colour: documents.isComplete
                  ? KhadraColors.accent
                  : KhadraColors.warn,
            ),
      onTap: () => context.push(Routes.documents),
    );
  }
}

class _LanguageGroup extends ConsumerWidget {
  const _LanguageGroup();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);
    final current = ref.watch(localeProvider);

    return _Group(
      title: l10n.profileLanguage,
      children: [
        RadioGroup<String?>(
          groupValue: current?.languageCode,
          onChanged: (value) => ref.read(localeProvider.notifier).set(
                value == null ? null : Locale(value),
              ),
          child: const Column(
            children: [
              _LanguageOption(value: 'en'),
              _LanguageOption(value: 'ar'),
            ],
          ),
        ),
      ],
    );
  }
}

class _LanguageOption extends StatelessWidget {
  const _LanguageOption({required this.value});

  final String value;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return RadioListTile<String?>(
      value: value,
      title: Text(
        value == 'ar' ? l10n.profileLanguageArabic : l10n.profileLanguageEnglish,
      ),
      // The label is written in its OWN language, always: somebody who has the app
      // in the wrong language has to be able to find their way out of it.
      contentPadding: const EdgeInsets.symmetric(horizontal: Space.lg),
    );
  }
}

class _Group extends StatelessWidget {
  const _Group({required this.title, required this.children});

  final String title;
  final List<Widget> children;

  @override
  Widget build(BuildContext context) => Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(
                Space.lg, Space.lg, Space.lg, Space.sm),
            child: Text(
              title,
              style: const TextStyle(
                fontSize: 13,
                fontWeight: FontWeight.w700,
                color: KhadraColors.neutral600,
                letterSpacing: 0.4,
              ),
            ),
          ),
          Container(
            color: KhadraColors.surface,
            child: Column(children: children),
          ),
        ],
      );
}

class _Row extends StatelessWidget {
  const _Row({
    required this.icon,
    required this.label,
    required this.onTap,
    this.trailing,
  });

  final IconData icon;
  final String label;
  final VoidCallback onTap;
  final Widget? trailing;

  @override
  Widget build(BuildContext context) => ListTile(
        leading: Icon(icon, color: KhadraColors.neutral600),
        title: Text(label, style: const TextStyle(fontSize: 15)),
        trailing: Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            if (trailing != null) ...[
              trailing!,
              const SizedBox(width: Space.sm),
            ],
            const Icon(Icons.chevron_right, color: KhadraColors.neutral400),
          ],
        ),
        onTap: onTap,
      );
}
