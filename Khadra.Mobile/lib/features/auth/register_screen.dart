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

class RegisterScreen extends ConsumerStatefulWidget {
  const RegisterScreen({super.key});

  @override
  ConsumerState<RegisterScreen> createState() => _RegisterScreenState();
}

class _RegisterScreenState extends ConsumerState<RegisterScreen> {
  final _formKey = GlobalKey<FormState>();
  final _name = TextEditingController();
  final _email = TextEditingController();
  final _phone = TextEditingController();
  final _password = TextEditingController();

  DateTime? _dateOfBirth;
  bool _isForeignNational = false;
  bool _busy = false;
  String? _error;
  String? _dateError;

  @override
  void dispose() {
    _name.dispose();
    _email.dispose();
    _phone.dispose();
    _password.dispose();
    super.dispose();
  }

  Future<void> _pickDateOfBirth(int minimumAge) async {
    final now = DateTime.now();

    // Opens at the youngest date that would be accepted, so the wheel starts
    // where the answer is rather than at today, which is never valid.
    final latestAllowed = DateTime(now.year - minimumAge, now.month, now.day);

    final picked = await showDatePicker(
      context: context,
      initialDate: _dateOfBirth ?? latestAllowed,
      firstDate: DateTime(now.year - 100),
      lastDate: latestAllowed,
      initialDatePickerMode: DatePickerMode.year,
    );

    if (picked != null) {
      setState(() {
        _dateOfBirth = picked;
        _dateError = null;
      });
    }
  }

  Future<void> _submit(int? minimumAge) async {
    final l10n = AppLocalizations.of(context);
    final formValid = _formKey.currentState?.validate() ?? false;

    // The date of birth is asked for exactly when the platform has an age rule.
    // Null minimumAge is a real shipping state -- the owner has set no limit --
    // and demanding a birthday for a check nobody makes would be a field for
    // nothing.
    final needsDate = minimumAge != null && _dateOfBirth == null;
    if (needsDate) setState(() => _dateError = l10n.validationDateOfBirth);
    if (!formValid || needsDate) return;

    setState(() {
      _busy = true;
      _error = null;
    });

    try {
      final registered = await ref.read(apiProvider).register(
            email: _email.text.trim(),
            password: _password.text,
            fullName: _name.text.trim(),
            phone: _phone.text.trim(),
            dateOfBirth: _dateOfBirth,
            isForeignNational: _isForeignNational,
          );

      if (!mounted) return;

      context.go(
        Uri(
          path: Routes.verifyEmail,
          queryParameters: {
            'email': registered.email,
            // False does not mean registration failed. It means nothing is on its
            // way to that inbox, and the next screen must offer another link
            // rather than tell somebody to wait for one that is not coming.
            if (!registered.verificationEmailSent) 'undelivered': '1',
          },
        ).toString(),
      );
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
    final config = ref.watch(appConfigProvider);
    final formats = ref.watch(formatsProvider);
    final minimumAge = config.valueOrNull?.minimumRenterAge;

    return AuthScaffold(
      title: l10n.authCreateAccountTitle,
      subtitle: l10n.authCreateAccountSubtitle,
      children: [
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
                  controller: _name,
                  label: l10n.authFullName,
                  textInputAction: TextInputAction.next,
                  autofillHints: const [AutofillHints.name],
                  maxLength: 150,
                  validator: (value) => Validate.required(l10n, value),
                  enabled: !_busy,
                ),
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
                KhadraField(
                  controller: _phone,
                  label: l10n.authPhone,
                  hint: l10n.authPhoneHint,
                  keyboardType: TextInputType.phone,
                  textInputAction: TextInputAction.next,
                  autofillHints: const [AutofillHints.telephoneNumber],
                  validator: (value) => Validate.phone(l10n, value),
                  forceLtr: true,
                  maxLength: 32,
                  enabled: !_busy,
                ),
                KhadraPasswordField(
                  controller: _password,
                  label: l10n.authPassword,
                  helper: l10n.authPasswordRules,
                  autofillHints: const [AutofillHints.newPassword],
                  validator: (value) => Validate.password(l10n, value),
                ),
              ],
            ),
          ),
        ),

        // Shown only when the platform actually has an age rule, and the figure is
        // the server's -- not a 21 typed into this file.
        if (minimumAge != null) ...[
          _DateOfBirthField(
            value: _dateOfBirth,
            label: l10n.authDateOfBirth,
            display: _dateOfBirth == null || formats == null
                ? null
                : formats.longDate(_dateOfBirth!),
            helper: l10n.authMinimumAge(minimumAge),
            error: _dateError,
            onTap: _busy ? null : () => _pickDateOfBirth(minimumAge),
          ),
          const SizedBox(height: Space.lg),
        ],

        // Spec 5.1: a foreign renter files a passport rather than a national ID.
        // The choice changes which document the documents screen asks for.
        Padding(
          padding: const EdgeInsets.only(bottom: Space.lg),
          child: SwitchListTile.adaptive(
            value: _isForeignNational,
            onChanged: _busy
                ? null
                : (value) => setState(() => _isForeignNational = value),
            title: Text(l10n.authForeignNational),
            subtitle: Text(
              l10n.authForeignNationalHelp,
              style: const TextStyle(fontSize: 13),
            ),
            contentPadding: EdgeInsets.zero,
            activeThumbColor: KhadraColors.accent,
          ),
        ),

        // Until the config answers, the form does not know whether to ask for a
        // date of birth, so submitting early could send a registration the server
        // refuses on a field the customer was never shown.
        KhadraSubmitButton(
          label: l10n.authSignUp,
          busy: _busy || config.isLoading,
          onPressed: () => _submit(minimumAge),
        ),
        const SizedBox(height: Space.xl),
        Row(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            Text(
              l10n.authAlreadyHaveAccount,
              style: const TextStyle(color: KhadraColors.neutral600),
            ),
            TextButton(
              onPressed: _busy ? null : () => context.go(Routes.signIn),
              child: Text(l10n.authSignIn),
            ),
          ],
        ),
      ],
    );
  }
}

class _DateOfBirthField extends StatelessWidget {
  const _DateOfBirthField({
    required this.value,
    required this.label,
    required this.display,
    required this.helper,
    required this.error,
    required this.onTap,
  });

  final DateTime? value;
  final String label;
  final String? display;
  final String helper;
  final String? error;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) => InkWell(
        onTap: onTap,
        borderRadius: Radii.field,
        child: InputDecorator(
          decoration: InputDecoration(
            labelText: label,
            helperText: helper,
            helperMaxLines: 3,
            errorText: error,
            suffixIcon: const Icon(Icons.calendar_today_outlined, size: 20),
          ),
          child: Text(
            display ?? '—',
            style: TextStyle(
              fontSize: 16,
              color: value == null ? KhadraColors.neutral500 : KhadraColors.text,
            ),
          ),
        ),
      );
}
