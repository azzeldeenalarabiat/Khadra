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

/// Reached from a link in an email. The token is the whole authority here, which
/// is why the screen refuses to render a form without one rather than showing a
/// password box that could never work.
class ResetPasswordScreen extends ConsumerStatefulWidget {
  const ResetPasswordScreen({super.key, this.token});

  final String? token;

  @override
  ConsumerState<ResetPasswordScreen> createState() =>
      _ResetPasswordScreenState();
}

class _ResetPasswordScreenState extends ConsumerState<ResetPasswordScreen> {
  final _formKey = GlobalKey<FormState>();
  final _password = TextEditingController();
  final _confirm = TextEditingController();

  bool _busy = false;
  bool _done = false;
  String? _error;

  @override
  void dispose() {
    _password.dispose();
    _confirm.dispose();
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
      await ref.read(apiProvider).resetPassword(widget.token!, _password.text);
      if (!mounted) return;
      setState(() {
        _busy = false;
        _done = true;
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
    final token = widget.token;

    if (token == null || token.isEmpty) {
      return AuthScaffold(
        title: l10n.authResetPasswordTitle,
        children: [
          KhadraNotice(title: l10n.errorAuthInvalidToken, tone: NoticeTone.bad),
          const SizedBox(height: Space.lg),
          OutlinedButton(
            onPressed: () => context.go(Routes.forgotPassword),
            child: Text(l10n.authSendResetLink),
          ),
        ],
      );
    }

    if (_done) {
      return AuthScaffold(
        title: l10n.authResetPasswordTitle,
        children: [
          KhadraNotice(
            title: l10n.authResetPasswordDone,
            tone: NoticeTone.accent,
          ),
          const SizedBox(height: Space.lg),
          KhadraSubmitButton(
            label: l10n.authSignIn,
            onPressed: () => context.go(Routes.signIn),
          ),
        ],
      );
    }

    return AuthScaffold(
      title: l10n.authResetPasswordTitle,
      children: [
        if (_error != null) ...[
          KhadraNotice(title: _error!, tone: NoticeTone.bad),
          const SizedBox(height: Space.lg),
        ],
        Form(
          key: _formKey,
          child: Column(
            children: [
              KhadraPasswordField(
                controller: _password,
                label: l10n.authNewPassword,
                helper: l10n.authPasswordRules,
                validator: (value) => Validate.password(l10n, value),
              ),
              KhadraPasswordField(
                controller: _confirm,
                label: l10n.authConfirmPassword,
                onSubmitted: _submit,
                validator: (value) => value == _password.text
                    ? null
                    : l10n.validationPasswordMatch,
              ),
            ],
          ),
        ),
        KhadraSubmitButton(
          label: l10n.actionSave,
          busy: _busy,
          onPressed: _submit,
        ),
      ],
    );
  }
}
