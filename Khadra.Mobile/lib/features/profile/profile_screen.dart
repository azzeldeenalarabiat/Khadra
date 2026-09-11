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
      appBar: AppBar(title: KhadraLargeTitle(l10n.profileTitle)),
      body: RefreshIndicator(
        // The account and the document checklist are BOTH read here, and both
        // go stale while the app is open: an email verified in a browser, or a
        // licence approved by the office, changes what this screen should say.
        // Without this the only way to see either was to sign out.
        onRefresh: () async {
          ref.invalidate(myDocumentsProvider);
          await ref.read(sessionProvider.notifier).reload();
        },
        child: ListView(
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
                  _Row(
                    icon: Icons.favorite_border,
                    label: l10n.shortlistTitle,
                    onTap: () => context.push(Routes.shortlist),
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
              _Group(
                title: l10n.reputationGroupTitle,
                children: [
                  // No badge. A badge would run the reputation reader on every
                  // visit to this tab, for a figure that changes a few times a
                  // year — and a customer with one bad mark would carry it on
                  // every screen they open.
                  _Row(
                    icon: Icons.workspace_premium_outlined,
                    label: l10n.reputationTitle,
                    onTap: () => context.push(Routes.reputation),
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
      // The DIALOG's context, not the screen's.
      //
      // `showDialog` pushes onto the root navigator, but `Navigator.of` walks up
      // from whatever context it is given -- and from a screen inside the tab
      // shell that finds the SHELL's navigator, not the root. Popping that one
      // tears the profile page off its branch instead of dismissing the dialog,
      // leaving the shell with an empty stack and the app with a blank screen.
      builder: (dialogContext) => AlertDialog(
        title: Text(allDevices
            ? l10n.profileSignOutEverywhereConfirm
            : l10n.profileSignOutConfirm),
        actions: [
          TextButton(
            onPressed: () => Navigator.of(dialogContext).pop(false),
            child: Text(l10n.actionCancel),
          ),
          FilledButton(
            onPressed: () => Navigator.of(dialogContext).pop(true),
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
                  ? l10n.documentsBadgeComplete
                  : l10n.documentsBadgeMissing,
              colour: documents.isComplete
                  ? KhadraColors.accent
                  : KhadraColors.warn,
            ),
      onTap: () => context.push(Routes.documents),
    );
  }
}

/// The language switch, including the state it starts in.
///
/// **"Follow the device" is an option, not the absence of one.** Null is the
/// default and the right one — a phone set to Arabic should open in Arabic
/// without being asked — but rendering only `en` and `ar` against a null value
/// left a fresh install showing two radios with NEITHER selected, which reads as
/// a broken control rather than as a sensible default. It is also the only way
/// back to following the device once a language has been chosen.
class _LanguageGroup extends ConsumerWidget {
  const _LanguageGroup();

  /// The sentinel for "follow the device". `RadioGroup` distinguishes options by
  /// value, so the third one needs a value of its own rather than null — which
  /// is exactly what a stored language being absent looks like.
  static const _system = '';

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);
    final current = ref.watch(localeProvider);

    return _Group(
      title: l10n.profileLanguage,
      children: [
        RadioGroup<String>(
          groupValue: current?.languageCode ?? _system,
          onChanged: (value) => ref.read(localeProvider.notifier).set(
                value == null || value == _system ? null : Locale(value),
              ),
          child: const Column(
            children: [
              _LanguageOption(value: _system),
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
    return RadioListTile<String>(
      value: value,
      // A named language is written in its OWN language, always: somebody who
      // has the app in the wrong one has to be able to find their way out of it.
      // "Follow the device" names no language, so it is translated like any other
      // sentence.
      title: Text(switch (value) {
        'ar' => l10n.profileLanguageArabic,
        'en' => l10n.profileLanguageEnglish,
        _ => l10n.profileLanguageSystem,
      }),
      contentPadding: const EdgeInsets.symmetric(horizontal: Space.lg),
    );
  }
}

class _Group extends StatelessWidget {
  const _Group({required this.title, required this.children});

  final String title;
  final List<Widget> children;

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.fromLTRB(Space.lg, Space.lg, Space.lg, 0),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            // A quiet uppercase label ABOVE the group rather than a grey band
            // across the screen: the design lets the card do the separating, and
            // the label only has to say what the card is.
            Padding(
              padding: const EdgeInsetsDirectional.only(start: 2, bottom: 9),
              child: Text(
                title.toUpperCase(),
                style: const TextStyle(
                  fontSize: 11,
                  fontWeight: FontWeight.w700,
                  color: KhadraColors.neutral500,
                  letterSpacing: 0.7,
                ),
              ),
            ),
            // A Material, not a Container. A ColoredBox here paints over the
            // Scaffold canvas that the tiles ink onto, so every row in the group
            // would swallow its own ripple -- and a tap with no feedback reads as
            // a tap that did not land. `clipBehavior` is what keeps the first and
            // last rows' ink inside the rounded corners.
            Material(
              color: KhadraColors.surface,
              shape: const RoundedRectangleBorder(
                borderRadius: Radii.card,
                side: BorderSide(color: KhadraColors.neutral200),
              ),
              clipBehavior: Clip.antiAlias,
              child: Column(
                children: [
                  for (var i = 0; i < children.length; i++) ...[
                    if (i > 0)
                      const Divider(
                        height: 1,
                        indent: Space.lg,
                        endIndent: Space.lg,
                        color: KhadraColors.divider,
                      ),
                    children[i],
                  ],
                ],
              ),
            ),
          ],
        ),
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
        leading: Icon(icon, color: KhadraColors.neutral600, size: 20),
        horizontalTitleGap: Space.md,
        minLeadingWidth: 20,
        contentPadding: const EdgeInsetsDirectional.symmetric(horizontal: Space.lg),
        title: Text(
          label,
          style: const TextStyle(fontSize: 14, fontWeight: FontWeight.w600),
        ),
        // Bounded on purpose. A ListTile gives its trailing widget as much width
        // as it asks for, so an unbounded one crushes the title -- which is how
        // "My documents" ended up rendering one character per line.
        trailing: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 150),
          child: Row(
            mainAxisSize: MainAxisSize.min,
            children: [
              if (trailing != null) ...[
                Flexible(child: trailing!),
                const SizedBox(width: Space.sm),
              ],
              const Icon(Icons.chevron_right, color: KhadraColors.neutral400),
            ],
          ),
        ),
        onTap: onTap,
      );
}
