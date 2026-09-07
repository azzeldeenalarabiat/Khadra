import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/api/api_failure.dart';
import '../../core/api/api_failure_messages.dart';
import '../../core/providers.dart';
import '../../core/router.dart';
import '../../core/session/session_controller.dart';
import '../../core/theme/khadra_theme.dart';
import '../../core/widgets/khadra_widgets.dart';
import '../../l10n/app_localizations.dart';
import 'auth_form_widgets.dart';

class SignInScreen extends ConsumerStatefulWidget {
  const SignInScreen({super.key, this.next});

  /// Where the customer was heading when they were asked to sign in.
  final String? next;

  @override
  ConsumerState<SignInScreen> createState() => _SignInScreenState();
}

class _SignInScreenState extends ConsumerState<SignInScreen> {
  final _formKey = GlobalKey<FormState>();
  final _email = TextEditingController();
  final _password = TextEditingController();

  bool _busy = false;
  String? _error;

  @override
  void dispose() {
    _email.dispose();
    _password.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    if (!(_formKey.currentState?.validate() ?? false)) return;

    setState(() {
      _busy = true;
      _error = null;
    });

    final l10n = AppLocalizations.of(context);

    try {
      final user = await ref
          .read(sessionProvider.notifier)
          .signIn(_email.text.trim(), _password.text);

      if (!mounted) return;

      // An unverified account signs in perfectly well — it simply cannot book —
      // so the app takes them straight to the screen that explains why, rather
      // than letting them find out at the booking button.
      if (!user.isEmailVerified) {
        context.go(
          Uri(
            path: Routes.verifyEmail,
            queryParameters: {'email': user.email},
          ).toString(),
        );
        return;
      }

      context.go(widget.next ?? Routes.search);
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

    // Why the last session ended, if it ended on its own. Somebody thrown out
    // mid-task deserves to be told which of the two things happened.
    final endedReason = ref.watch(sessionProvider).endedReason;

    return AuthScaffold(
      title: l10n.authWelcomeTitle,
      subtitle: l10n.authWelcomeSubtitle,
      leading: IconButton(
        icon: const Icon(Icons.close),
        onPressed: () => context.go(Routes.search),
        tooltip: l10n.actionClose,
      ),
      children: [
        if (endedReason != null && _error == null) ...[
          KhadraNotice(
            title: endedReason == SessionEndReason.suspended
                ? l10n.errorAuthAccountSuspended
                : l10n.authSessionExpired,
            tone: endedReason == SessionEndReason.suspended
                ? NoticeTone.bad
                : NoticeTone.warn,
          ),
          const SizedBox(height: Space.lg),
        ],
        if (_error != null) ...[
          KhadraNotice(title: _error!, tone: NoticeTone.bad),
          const SizedBox(height: Space.lg),
        ],
        Form(
          key: _formKey,
          child: AutofillGroup(
            child: Column(
              children: [
                KhadraField(
                  controller: _email,
                  label: l10n.authEmail,
                  keyboardType: TextInputType.emailAddress,
                  textInputAction: TextInputAction.next,
                  autofillHints: const [AutofillHints.email],
                  validator: (value) => Validate.email(l10n, value),
                  forceLtr: true,
                  enabled: !_busy,
                ),
                KhadraPasswordField(
                  controller: _password,
                  label: l10n.authPassword,
                  textInputAction: TextInputAction.done,
                  onSubmitted: _submit,
                  autofillHints: const [AutofillHints.password],
                  validator: (value) =>
                      Validate.required(l10n, value),
                ),
              ],
            ),
          ),
        ),
        Align(
          alignment: AlignmentDirectional.centerStart,
          child: TextButton(
            onPressed: _busy ? null : () => context.push(Routes.forgotPassword),
            child: Text(l10n.authForgotPassword),
          ),
        ),
        const SizedBox(height: Space.md),
        KhadraSubmitButton(
          label: l10n.authSignIn,
          busy: _busy,
          onPressed: _submit,
        ),
        const SizedBox(height: Space.xl),
        Row(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            Text(
              l10n.authNoAccount,
              style: const TextStyle(color: KhadraColors.neutral600),
            ),
            TextButton(
              onPressed: _busy ? null : () => context.push(Routes.register),
              child: Text(l10n.authSignUp),
            ),
          ],
        ),
      ],
    );
  }
}
