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
import 'auth_form_widgets.dart';

/// Two jobs on one screen: consume a verification token from a link, or tell
/// somebody a link is waiting in their inbox.
///
/// Verification matters more here than it looks. Until push notifications exist,
/// a dealer's approval reaches a customer by email; an unverified address is a
/// booking that expires unread, with the gallery's decision wasted and a car held
/// for nothing meanwhile. That is why the server refuses a booking without it.
class VerifyEmailScreen extends ConsumerStatefulWidget {
  const VerifyEmailScreen({
    super.key,
    this.token,
    this.email,
    this.undelivered = false,
  });

  final String? token;
  final String? email;

  /// The account was created but the verification email did NOT go out.
  ///
  /// A real state, not a failure: registration succeeded and the transport did
  /// not. Saying "check your inbox" here would send somebody to watch for a
  /// message that is not coming.
  final bool undelivered;

  @override
  ConsumerState<VerifyEmailScreen> createState() => _VerifyEmailScreenState();
}

class _VerifyEmailScreenState extends ConsumerState<VerifyEmailScreen> {
  bool _busy = false;
  bool _verified = false;
  bool _resent = false;
  String? _error;

  /// Cleared by a successful resend: once one is on its way, the sentence saying
  /// none was sent has stopped being true.
  late bool _undelivered = widget.undelivered;

  @override
  void initState() {
    super.initState();
    if (widget.token != null && widget.token!.isNotEmpty) {
      WidgetsBinding.instance.addPostFrameCallback((_) => _verify(widget.token!));
    }
  }

  Future<void> _verify(String token) async {
    setState(() {
      _busy = true;
      _error = null;
    });

    final l10n = AppLocalizations.of(context);

    try {
      await ref.read(apiProvider).verifyEmail(token);
      // Re-read the account so `canBook` becomes true without another sign-in.
      await ref.read(sessionProvider.notifier).reload();
      if (!mounted) return;
      setState(() {
        _busy = false;
        _verified = true;
      });
    } on ApiFailure catch (failure) {
      if (!mounted) return;
      setState(() {
        _busy = false;
        _error = failure.messageFor(l10n);
      });
    }
  }

  Future<void> _resend(String email) async {
    setState(() {
      _busy = true;
      _error = null;
    });

    final l10n = AppLocalizations.of(context);

    try {
      await ref.read(apiProvider).resendVerification(email);
      if (!mounted) return;
      setState(() {
        _busy = false;
        _resent = true;
        _undelivered = false;
      });
    } on ApiFailure catch (failure) {
      if (!mounted) return;
      setState(() {
        _busy = false;
        _error = failure.messageFor(l10n);
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final session = ref.watch(sessionProvider);
    final email = widget.email ?? session.user?.email ?? '';

    // The customer may already have verified elsewhere -- another device, a
    // browser -- and come back here. Reading it from the account rather than only
    // from this screen's own attempt is what stops that showing as unverified.
    final verified = _verified || (session.user?.isEmailVerified ?? false);

    if (verified) {
      return AuthScaffold(
        title: l10n.authVerifiedTitle,
        subtitle: l10n.authVerifiedBody,
        children: [
          const SizedBox(height: Space.sm),
          KhadraSubmitButton(
            label: l10n.actionContinue,
            onPressed: () => context.go(Routes.search),
          ),
        ],
      );
    }

    return AuthScaffold(
      title: l10n.authVerifyEmailTitle,
      // No "we sent a link to …" when nothing was sent. The account exists and
      // the link has to be asked for again, which the button below does.
      subtitle: email.isEmpty || _undelivered
          ? null
          : l10n.authVerifyEmailBody(email),
      leading: IconButton(
        icon: const Icon(Icons.close),
        onPressed: () => context.go(Routes.search),
        tooltip: l10n.actionClose,
      ),
      children: [
        if (_undelivered) ...[
          KhadraNotice(
            title: l10n.authEmailNotDelivered,
            tone: NoticeTone.warn,
            icon: Icons.unsubscribe_outlined,
          ),
          const SizedBox(height: Space.lg),
        ],
        KhadraNotice(
          title: l10n.authVerifyEmailWhy,
          tone: NoticeTone.neutral,
          icon: Icons.mark_email_unread_outlined,
        ),
        const SizedBox(height: Space.lg),
        if (_error != null) ...[
          KhadraNotice(title: _error!, tone: NoticeTone.bad),
          const SizedBox(height: Space.lg),
        ],
        if (_resent) ...[
          KhadraNotice(
            title: l10n.authVerificationResent,
            tone: NoticeTone.accent,
          ),
          const SizedBox(height: Space.lg),
        ],
        if (email.isNotEmpty)
          KhadraSubmitButton(
            label: l10n.authResendVerification,
            busy: _busy,
            icon: Icons.refresh,
            onPressed: () => _resend(email),
          ),
        const SizedBox(height: Space.md),
        OutlinedButton(
          onPressed: _busy
              ? null
              : () async {
                  // The link is opened in a mail app, not here, so the way back is
                  // to re-read the account.
                  await ref.read(sessionProvider.notifier).reload();
                  if (context.mounted) setState(() {});
                },
          child: Text(l10n.actionRetry),
        ),
        const SizedBox(height: Space.lg),
        Center(
          child: TextButton(
            onPressed: () => context.go(Routes.search),
            child: Text(l10n.authBrowseInstead),
          ),
        ),
      ],
    );
  }
}
