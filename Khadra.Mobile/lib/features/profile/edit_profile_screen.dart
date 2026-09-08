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

/// Name and phone. Deliberately not email.
///
/// The address is how somebody signs in and where a password reset is sent, so
/// moving it on the strength of a live session alone would hand the account to
/// anyone holding an unlocked phone. It needs a verified change flow, which the
/// platform has not built — pre-launch checklist items 44 and 72 — and the screen
/// says so rather than showing a field that would be refused.
class EditProfileScreen extends ConsumerStatefulWidget {
  const EditProfileScreen({super.key});

  @override
  ConsumerState<EditProfileScreen> createState() => _EditProfileScreenState();
}

class _EditProfileScreenState extends ConsumerState<EditProfileScreen> {
  final _formKey = GlobalKey<FormState>();
  late final TextEditingController _name;
  late final TextEditingController _phone;

  bool _busy = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    final user = ref.read(sessionProvider).user;
    _name = TextEditingController(text: user?.fullName ?? '');
    _phone = TextEditingController(text: user?.phone ?? '');
  }

  @override
  void dispose() {
    _name.dispose();
    _phone.dispose();
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
      final updated = await ref.read(apiProvider).updateProfile(
            fullName: _name.text.trim(),
            phone: _phone.text.trim(),
          );

      // The session's copy is replaced with the SERVER's, which matters because
      // the phone number comes back normalised: "0791234567" is stored and
      // displayed as "+962791234567", and showing what was typed would disagree
      // with what the platform holds.
      ref.read(sessionProvider.notifier).applyUser(updated);

      if (!mounted) return;
      showKhadraMessage(context, l10n.profileSaved);
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
    final user = ref.watch(sessionProvider).user;

    return Scaffold(
      appBar: AppBar(title: Text(l10n.profileEdit)),
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
                KhadraField(
                  controller: _name,
                  label: l10n.authFullName,
                  maxLength: 150,
                  textInputAction: TextInputAction.next,
                  validator: (value) => Validate.required(l10n, value),
                  enabled: !_busy,
                ),
                KhadraField(
                  controller: _phone,
                  label: l10n.authPhone,
                  hint: l10n.authPhoneHint,
                  keyboardType: TextInputType.phone,
                  textInputAction: TextInputAction.done,
                  onSubmitted: _submit,
                  validator: (value) => Validate.phone(l10n, value),
                  forceLtr: true,
                  maxLength: 32,
                  enabled: !_busy,
                ),
              ],
            ),
          ),

          // Shown read-only, with the reason. A disabled field with no explanation
          // reads as a bug.
          KhadraCard(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  l10n.authEmail,
                  style: const TextStyle(
                      color: KhadraColors.neutral600, fontSize: 13),
                ),
                const SizedBox(height: 4),
                LatinRun(
                  user?.email ?? '',
                  style: const TextStyle(
                      fontSize: 15, fontWeight: FontWeight.w600),
                ),
                const SizedBox(height: Space.sm),
                Text(
                  l10n.profileEmailFixed,
                  style: const TextStyle(
                      color: KhadraColors.neutral600, fontSize: 12, height: 1.5),
                ),
              ],
            ),
          ),

          const SizedBox(height: Space.xl),
          KhadraSubmitButton(
            label: l10n.actionSaveChanges,
            busy: _busy,
            onPressed: _submit,
          ),
        ],
      ),
    );
  }
}
