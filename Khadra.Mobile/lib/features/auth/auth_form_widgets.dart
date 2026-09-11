import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../api/dtos.dart';
import '../../core/theme/khadra_theme.dart';
import '../../core/widgets/khadra_widgets.dart';
import '../../l10n/app_localizations.dart';

/// The frame every auth screen sits in: the mark, a title, and the form.
class AuthScaffold extends StatelessWidget {
  const AuthScaffold({
    super.key,
    required this.title,
    required this.children,
    this.subtitle,
    this.showLogo = true,
    this.leading,
  });

  final String title;
  final String? subtitle;
  final List<Widget> children;
  final bool showLogo;
  final Widget? leading;

  @override
  Widget build(BuildContext context) => Scaffold(
        backgroundColor: KhadraColors.surface,
        appBar: AppBar(
          backgroundColor: KhadraColors.surface,
          leading: leading,
        ),
        body: SafeArea(
          child: Center(
            child: SingleChildScrollView(
              padding: const EdgeInsets.fromLTRB(
                  Space.xl, Space.sm, Space.xl, Space.bottomInset),
              child: ConstrainedBox(
                // A phone form on a tablet or a desktop browser should not stretch
                // to a metre wide; the app builds for web too.
                constraints: const BoxConstraints(maxWidth: 440),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    if (showLogo) ...[
                      const Center(child: KhadraLogo(size: 72)),
                      const SizedBox(height: Space.xl),
                    ],
                    Text(
                      title,
                      style: const TextStyle(
                        fontSize: 26,
                        height: 1.2,
                        fontWeight: FontWeight.w800,
                        letterSpacing: -0.6,
                        color: KhadraColors.text,
                      ),
                    ),
                    if (subtitle != null) ...[
                      const SizedBox(height: Space.sm),
                      Text(
                        subtitle!,
                        style: const TextStyle(
                          fontSize: 14,
                          height: 1.5,
                          fontWeight: FontWeight.w500,
                          color: KhadraColors.neutral600,
                        ),
                      ),
                    ],
                    const SizedBox(height: Space.xl),
                    ...children,
                  ],
                ),
              ),
            ),
          ),
        ),
      );
}

/// A labelled text field with the app's validation vocabulary.
class KhadraField extends StatelessWidget {
  const KhadraField({
    super.key,
    required this.controller,
    required this.label,
    this.hint,
    this.helper,
    this.keyboardType,
    this.obscure = false,
    this.validator,
    this.textInputAction,
    this.onSubmitted,
    this.onChanged,
    this.maxLength,
    this.maxLines = 1,
    this.enabled = true,
    this.autofillHints,
    this.inputFormatters,
    this.suffix,
    this.errorText,
    this.forceLtr = false,
    this.autofocus = false,
  });

  final TextEditingController controller;
  final String label;
  final String? hint;
  final String? helper;
  final TextInputType? keyboardType;
  final bool obscure;
  final String? Function(String?)? validator;
  final TextInputAction? textInputAction;
  final VoidCallback? onSubmitted;
  final ValueChanged<String>? onChanged;
  final int? maxLength;
  final int maxLines;
  final bool enabled;
  final Iterable<String>? autofillHints;
  final List<TextInputFormatter>? inputFormatters;
  final Widget? suffix;
  final String? errorText;

  /// Emails, phone numbers and passwords are Latin whatever the interface
  /// language is. Typing one into an RTL field puts the caret on the wrong side
  /// and reorders what has been typed so far.
  final bool forceLtr;

  /// For a field that IS the dialog it sits in. Never on a form with several.
  final bool autofocus;

  @override
  Widget build(BuildContext context) {
    final field = TextFormField(
      controller: controller,
      keyboardType: keyboardType,
      obscureText: obscure,
      validator: validator,
      textInputAction: textInputAction,
      onFieldSubmitted: onSubmitted == null ? null : (_) => onSubmitted!(),
      onChanged: onChanged,
      maxLength: maxLength,
      maxLines: obscure ? 1 : maxLines,
      enabled: enabled,
      autofocus: autofocus,
      autofillHints: autofillHints,
      inputFormatters: inputFormatters,
      textDirection: forceLtr ? TextDirection.ltr : null,
      decoration: InputDecoration(
        hintText: hint,
        helperText: helper,
        helperMaxLines: 3,
        errorText: errorText,
        errorMaxLines: 3,
        suffixIcon: suffix,
        counterText: '',
      ),
    );

    // The label sits ABOVE the box, not inside it. The design draws it that way
    // on every form it has, and it is the shape that survives Arabic: a floating
    // label animating over a right-to-left field lands on the wrong end of it,
    // and a long Arabic label shrinks to nothing on focus.
    return Padding(
      padding: const EdgeInsets.only(bottom: Space.lg),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Padding(
            padding: const EdgeInsetsDirectional.only(start: 2, bottom: 7),
            child: Text(
              label,
              style: const TextStyle(
                fontSize: 12,
                fontWeight: FontWeight.w600,
                color: KhadraColors.neutral700,
              ),
            ),
          ),
          field,
        ],
      ),
    );
  }
}

/// A password field with a reveal toggle.
class KhadraPasswordField extends StatefulWidget {
  const KhadraPasswordField({
    super.key,
    required this.controller,
    required this.label,
    this.helper,
    this.validator,
    this.textInputAction,
    this.onSubmitted,
    this.autofillHints,
    this.errorText,
    this.maxLength,
  });

  final TextEditingController controller;
  final String label;
  final String? helper;
  final String? Function(String?)? validator;
  final TextInputAction? textInputAction;
  final VoidCallback? onSubmitted;
  final Iterable<String>? autofillHints;
  final String? errorText;

  /// The platform's own cap, from `/app-config`. Null leaves the field
  /// unbounded and lets the server refuse — better than a 72 typed in here,
  /// which is bcrypt's limit today and this app's guess tomorrow.
  final int? maxLength;

  @override
  State<KhadraPasswordField> createState() => _KhadraPasswordFieldState();
}

class _KhadraPasswordFieldState extends State<KhadraPasswordField> {
  bool _hidden = true;

  @override
  Widget build(BuildContext context) => KhadraField(
        controller: widget.controller,
        label: widget.label,
        helper: widget.helper,
        obscure: _hidden,
        validator: widget.validator,
        textInputAction: widget.textInputAction,
        onSubmitted: widget.onSubmitted,
        autofillHints: widget.autofillHints,
        errorText: widget.errorText,
        forceLtr: true,
        maxLength: widget.maxLength,
        suffix: IconButton(
          onPressed: () => setState(() => _hidden = !_hidden),
          icon: Icon(_hidden ? Icons.visibility_outlined : Icons.visibility_off_outlined),
          color: KhadraColors.neutral500,
        ),
      );
}

/// A submit button that shows it is working and cannot be pressed twice.
class KhadraSubmitButton extends StatelessWidget {
  const KhadraSubmitButton({
    super.key,
    required this.label,
    required this.onPressed,
    this.busy = false,
    this.icon,
  });

  final String label;
  final VoidCallback? onPressed;
  final bool busy;
  final IconData? icon;

  @override
  Widget build(BuildContext context) => FilledButton(
        onPressed: busy ? null : onPressed,
        child: busy
            ? const SizedBox(
                width: 20,
                height: 20,
                child: CircularProgressIndicator(
                  strokeWidth: 2,
                  color: Colors.white,
                ),
              )
            : Row(
                mainAxisSize: MainAxisSize.min,
                children: [
                  if (icon != null) ...[
                    Icon(icon, size: 18),
                    const SizedBox(width: Space.sm),
                  ],
                  Flexible(child: Text(label, textAlign: TextAlign.center)),
                ],
              ),
      );
}

/// The app's shared field rules, so two screens cannot disagree about what a valid
/// email is.
abstract final class Validate {
  static String? required(AppLocalizations l10n, String? value) =>
      (value == null || value.trim().isEmpty) ? l10n.validationRequired : null;

  static String? email(AppLocalizations l10n, String? value) {
    final text = value?.trim() ?? '';
    if (text.isEmpty) return l10n.validationRequired;
    // Deliberately loose. The server's EmailAddress value object is the authority,
    // and a client regex that is stricter than it refuses addresses the platform
    // would have accepted.
    final looksLikeOne = RegExp(r'^[^@\s]+@[^@\s]+\.[^@\s]+$').hasMatch(text);
    return looksLikeOne ? null : l10n.validationEmail;
  }

  static String? phone(AppLocalizations l10n, String? value) {
    final text = (value ?? '').replaceAll(RegExp(r'[\s-]'), '');
    if (text.isEmpty) return l10n.validationRequired;
    // Jordanian mobile, local or international. PhoneNumber.Create is the
    // authority and normalises 07… to +9627…; this only catches the obvious.
    final ok = RegExp(r'^(?:\+9627|07)\d{8}$').hasMatch(text);
    return ok ? null : l10n.validationPhone;
  }

  /// A password, judged against the PLATFORM's rule.
  ///
  /// [policy] comes from `/app-config`. Null means the config has not arrived —
  /// a reset-password deep link can render before it does — and the only honest
  /// answer then is to check that something was typed and let the server judge
  /// the rest. It must never fall back to a number, because a number here is a
  /// second copy of a configurable rule and the whole reason this takes a
  /// parameter.
  ///
  /// **Never call this on the sign-in screen.** The server deliberately checks
  /// only that a sign-in password is present: raising the minimum must not lock
  /// out somebody whose password predates it.
  static String? password(
    AppLocalizations l10n,
    String? value, {
    PasswordPolicy? policy,
  }) {
    final text = value ?? '';
    if (text.isEmpty) return l10n.validationRequired;
    if (policy == null) return null;

    if (text.length < policy.minimumLength) {
      return l10n.validationPasswordShort(policy.minimumLength);
    }
    if (text.length > policy.maximumLength) {
      return l10n.validationPasswordLong(policy.maximumLength);
    }
    if (policy.requiresLetter && !text.contains(RegExp('[A-Za-z]'))) {
      return l10n.validationPasswordLetter;
    }
    if (policy.requiresDigit && !text.contains(RegExp(r'\d'))) {
      return l10n.validationPasswordDigit;
    }
    if (!policy.allowsWhitespace && text.contains(RegExp(r'\s'))) {
      return l10n.validationPasswordSpaces;
    }
    return null;
  }

  /// The rule, as a sentence under the field.
  ///
  /// Composed from the flags rather than sent by the server: the field-level
  /// messages above have to be the app's anyway, and "{n} characters" in Arabic
  /// needs plural forms that a server-side interpolation cannot produce. Null
  /// when there is no policy to describe — better a field with no helper than a
  /// helper describing a rule nobody is applying.
  static String? passwordRules(AppLocalizations l10n, PasswordPolicy? policy) {
    if (policy == null) return null;
    if (policy.requiresLetter && policy.requiresDigit) {
      return l10n.authPasswordRules(policy.minimumLength);
    }
    return l10n.authPasswordRulesLengthOnly(policy.minimumLength);
  }

  static String? maxLength(AppLocalizations l10n, String? value, int max) =>
      (value != null && value.length > max) ? l10n.validationTooLong(max) : null;
}
