import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_failure.dart';
import '../../core/api/api_failure_messages.dart';
import '../../core/providers.dart';
import '../../core/router.dart';
import '../../core/theme/khadra_theme.dart';
import '../../core/widgets/khadra_widgets.dart';
import '../../l10n/app_localizations.dart';
import 'auth_form_widgets.dart';

class ForgotPasswordScreen extends ConsumerStatefulWidget {
  const ForgotPasswordScreen({super.key});

  @override
  ConsumerState<ForgotPasswordScreen> createState() =>
      _ForgotPasswordScreenState();
}

class _ForgotPasswordScreenState extends ConsumerState<ForgotPasswordScreen> {
  final _formKey = GlobalKey<FormState>();
  final _email = TextEditingController();

  bool _busy = false;
  bool _sent = false;
  String? _error;

  @override
  void dispose() {
    _email.dispose();
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
      await ref.read(apiProvider).forgotPassword(_email.text.trim());
      if (!mounted) return;
      setState(() {
        _busy = false;
        _sent = true;
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

    return AuthScaffold(
      title: l10n.authForgotPasswordTitle,
      subtitle: l10n.authForgotPasswordSubtitle,
      showLogo: false,
      children: [
        if (_error != null) ...[
          KhadraNotice(title: _error!, tone: NoticeTone.bad),
          const SizedBox(height: Space.lg),
        ],
        if (_sent) ...[
          // Deliberately says "IF that address has an account". The server answers
          // the same either way, and a screen that said "sent" would turn this
          // into a way of finding out who is registered.
          KhadraNotice(title: l10n.authResetSent, tone: NoticeTone.accent),
          const SizedBox(height: Space.lg),
          OutlinedButton(
            onPressed: () => khadraLeave(context, Routes.signIn),
            child: Text(l10n.actionBack),
          ),
        ] else ...[
          Form(
            key: _formKey,
            child: KhadraField(
              controller: _email,
              label: l10n.authEmail,
              keyboardType: TextInputType.emailAddress,
              textInputAction: TextInputAction.done,
              onSubmitted: _submit,
              validator: (value) => Validate.email(l10n, value),
              forceLtr: true,
              enabled: !_busy,
            ),
          ),
          KhadraSubmitButton(
            label: l10n.authSendResetLink,
            busy: _busy,
            onPressed: _submit,
          ),
        ],
      ],
    );
  }
}
