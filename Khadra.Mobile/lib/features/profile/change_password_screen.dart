import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_failure.dart';
import '../../core/api/api_failure_messages.dart';
import '../../core/providers.dart';
import '../../core/router.dart';
import '../../core/theme/khadra_theme.dart';
import '../../core/widgets/khadra_widgets.dart';
import '../../l10n/app_localizations.dart';
import '../auth/auth_form_widgets.dart';

/// Changing a password rotates the account's security stamp and revokes every
/// other refresh-token family, so the token this app is holding stops working on
/// its very next request.
///
/// The endpoint hands back a FRESH pair for exactly that reason, and adopting it
/// is not optional: without it, a customer is signed out for changing their own
/// password, which reads as the app breaking.
class ChangePasswordScreen extends ConsumerStatefulWidget {
  const ChangePasswordScreen({super.key});

  @override
  ConsumerState<ChangePasswordScreen> createState() =>
      _ChangePasswordScreenState();
}

class _ChangePasswordScreenState extends ConsumerState<ChangePasswordScreen> {
  final _formKey = GlobalKey<FormState>();
  final _current = TextEditingController();
  final _next = TextEditingController();
  final _confirm = TextEditingController();

  bool _busy = false;
  String? _error;

  @override
  void dispose() {
    _current.dispose();
    _next.dispose();
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
      final tokens = await ref
          .read(apiProvider)
          .changePassword(_current.text, _next.text);

      await ref.read(sessionProvider.notifier).adoptTokens(tokens);

      if (!mounted) return;
      showKhadraMessage(context, l10n.authChangePasswordDone);
      khadraLeave(context, Routes.profile);
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

    return Scaffold(
      appBar: AppBar(title: Text(l10n.authChangePassword)),
      body: ListView(
        padding: const EdgeInsets.fromLTRB(
            Space.lg, Space.lg, Space.lg, Space.bottomInset),
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
                  controller: _current,
                  label: l10n.authCurrentPassword,
                  validator: (value) => Validate.required(l10n, value),
                ),
                KhadraPasswordField(
                  controller: _next,
                  label: l10n.authNewPassword,
                  helper: l10n.authPasswordRules,
                  validator: (value) => Validate.password(l10n, value),
                ),
                KhadraPasswordField(
                  controller: _confirm,
                  label: l10n.authConfirmPassword,
                  onSubmitted: _submit,
                  validator: (value) =>
                      value == _next.text ? null : l10n.validationPasswordMatch,
                ),
              ],
            ),
          ),
          // Said before the tap, not discovered after it. Changing a password
          // rotates the security stamp, which revokes every OTHER family on the
          // account; this device survives only because the endpoint hands back a
          // fresh pair and the app adopts it.
          KhadraNotice(
            title: l10n.authChangePasswordSignsOutOthers,
            body: l10n.authChangePasswordSignsOutOthersBody,
            tone: NoticeTone.neutral,
            icon: Icons.devices_outlined,
          ),
          const SizedBox(height: Space.xl),
          KhadraSubmitButton(
            label: l10n.authChangePassword,
            busy: _busy,
            onPressed: _submit,
          ),
        ],
      ),
    );
  }
}
