import 'dart:async';

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

      // Somebody who has signed in has said how they want to use the app, even if
      // they arrived from a link and never saw the Get Started screen. Without this
      // a customer who signed out, signed back in and relaunched would be asked
      // again by a screen they had already answered.
      //
      // NOT AWAITED, and that is load-bearing. `signIn` has already moved the
      // session to signedIn, which wakes the router's refresh listener, which
      // re-runs the redirect for `/sign-in` and bounces it to `/search` — tearing
      // this screen down. Awaiting a platform-channel write in that gap lets the
      // rebuild land first, `mounted` goes false, and the `context.go` below never
      // runs: the customer who tapped a car's booking button and was asked to sign
      // in arrives on the search screen with the car lost. `choose` sets its state
      // synchronously and only the disk write is deferred, so nothing here waits
      // for a preference to be written, and it swallows its own failures.
      unawaited(ref.read(entryChoiceProvider.notifier).choose());

      if (!mounted) return;

      // An unverified account signs in perfectly well — it simply cannot book —
      // so the app takes them straight to the screen that explains why, rather
      // than letting them find out at the booking button.
      if (!user.isEmailVerified) {
        context.go(
          Uri(
            path: Routes.verifyEmail,
            queryParameters: {
              'email': user.email,
              // The destination travels THROUGH the detour. Dropping it here is
              // what sent somebody heading for a car's booking form to the search
              // screen once they had verified, having lost the car.
              if (widget.next != null) 'next': widget.next!,
            },
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
        // Back to Get Started when that is where this was opened from, and
        // otherwise to the app's entry point, which decides between Get Started and
        // Home the same way a launch does. Closing this used to go straight to the
        // catalogue, which on a fresh install skipped the screen the customer had
        // not answered yet.
        onPressed: () => khadraLeave(context, Routes.splash),
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
          // Re-validates as a field is corrected, so a message does not outlive the
          // mistake it described. Without it the error stays until the next submit:
          // "This is needed." sat under an email box that had just been filled in,
          // which reads as the form refusing what was typed.
          autovalidateMode: AutovalidateMode.onUserInteraction,
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
              // The destination travels here too. Somebody asked for an account on
              // the way to a car, who creates one instead of signing in, should
              // still end up at the car.
              onPressed: _busy
                  ? null
                  : () => context.push(routeWithNext(Routes.register, widget.next)),
              child: Text(l10n.authSignUp),
            ),
          ],
        ),
      ],
    );
  }
}
