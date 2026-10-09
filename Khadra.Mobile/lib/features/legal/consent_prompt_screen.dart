import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/dtos.dart';
import '../../core/api/api_failure.dart';
import '../../core/api/api_failure_messages.dart';
import '../../core/providers.dart';
import '../../core/router.dart';
import '../../core/theme/khadra_theme.dart';
import '../../core/widgets/khadra_widgets.dart';
import '../../core/widgets/language_menu.dart';
import '../../l10n/app_localizations.dart';
import 'legal_text_screen.dart';

/// The whole app, while the signed-in person has a legal text in force still to
/// accept (pre-launch items 224 and 238; the app's half arrives in 1.4.0).
///
/// Put up by the app's root in place of the router (`main.dart`), the way the update
/// screen is, so no route, tab or deep link can reach past it: there is nothing behind
/// it, and Back closes the app. Unlike the update screen it is answered: accepting
/// leaves nothing pending and the app comes back exactly where the router left it.
///
/// It offers exactly what resolves it, as the website's prompt does: the texts
/// (each in the version being accepted), the acceptance, and signing out. The list
/// is read UNCACHED from `/auth/me/legal-consents`, never from `/app-config`, so a
/// version published a moment ago is the one asked for; a version that changes
/// under the person is asked for again, a bounded number of times.
class ConsentPromptScreen extends ConsumerStatefulWidget {
  const ConsentPromptScreen({super.key, required this.onAccepted, this.htmlView});

  /// Called once nothing is pending any more.
  final VoidCallback onAccepted;

  /// How a text is drawn when opened (see [LegalTextScreen]); replaced in tests.
  final LegalHtmlView? htmlView;

  /// How many times a version changing under the person reloads the list before the
  /// screen stops and asks them to try again. A publish happens once; a loop would be
  /// a fault, and a fault must not spin.
  static const maxReloads = 2;

  @override
  ConsumerState<ConsentPromptScreen> createState() => _ConsentPromptScreenState();
}

class _ConsentPromptScreenState extends ConsumerState<ConsentPromptScreen> {
  List<LegalDocumentRef>? _pending;
  ApiFailure? _loadFailure;
  bool _loading = true;
  bool _agreed = false;
  bool _submitting = false;
  bool _signingOut = false;
  String? _notice;
  int _reloads = 0;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _loadFailure = null;
    });
    try {
      final consents = await ref.read(apiProvider).myLegalConsents();
      if (!mounted) return;
      if (consents.pending.isEmpty) {
        widget.onAccepted();
        return;
      }
      setState(() => _pending = consents.pending);
    } on ApiFailure catch (failure) {
      if (mounted) setState(() => _loadFailure = failure);
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _accept() async {
    final l10n = AppLocalizations.of(context);
    final pending = _pending;
    if (pending == null || _submitting) return;
    if (!_agreed) {
      setState(() => _notice = l10n.consentRequired);
      return;
    }
    setState(() {
      _submitting = true;
      _notice = null;
    });
    try {
      final after = await ref.read(apiProvider).acceptLegalTexts(
            versionIds: [for (final text in pending) text.versionId],
            language: ref.read(appLanguageProvider),
          );
      if (!mounted) return;
      if (after.pending.isEmpty) {
        widget.onAccepted();
        return;
      }
      // Another text came into force between the read and the acceptance.
      setState(() {
        _pending = after.pending;
        _agreed = false;
        _notice = l10n.consentChanged;
      });
    } on ApiFailure catch (failure) {
      if (!mounted) return;
      if (failure.code == 'legal.version_not_current' && _reloads < ConsentPromptScreen.maxReloads) {
        _reloads++;
        setState(() {
          _agreed = false;
          _notice = l10n.consentChanged;
        });
        await _load();
      } else {
        setState(() => _notice = failure.code == 'legal.version_not_current'
            ? l10n.consentFailed
            : failure.messageFor(l10n, config: ref.read(appConfigProvider).valueOrNull));
      }
    } finally {
      if (mounted) setState(() => _submitting = false);
    }
  }

  /// Signing out always resolves the prompt, with or without a network: the session ends
  /// on the phone whatever the server answers. Then Get Started, as Profile's sign-out.
  Future<void> _signOut() async {
    setState(() => _signingOut = true);
    await ref.read(sessionProvider.notifier).signOut();
    await ref.read(entryChoiceProvider.notifier).forget();
    ref.read(routerProvider).go(Routes.welcome);
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final text = Theme.of(context).textTheme;
    final pending = _pending;
    final busy = _submitting || _signingOut;

    return Scaffold(
      backgroundColor: KhadraColors.background,
      appBar: AppBar(
        automaticallyImplyLeading: false,
        backgroundColor: KhadraColors.background,
        actions: const [KhadraLanguageMenu()],
      ),
      body: SafeArea(
        child: Center(
          child: SingleChildScrollView(
            child: ConstrainedBox(
              constraints: const BoxConstraints(maxWidth: Space.measure),
              child: Padding(
                padding: const EdgeInsets.all(Space.xl),
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    Text(l10n.consentTitle, style: text.headlineSmall, textAlign: TextAlign.center),
                    const SizedBox(height: Space.sm),
                    Text(
                      l10n.consentBody,
                      style: text.bodyMedium?.copyWith(color: KhadraColors.neutral600),
                      textAlign: TextAlign.center,
                    ),
                    const SizedBox(height: Space.xl),
                    if (_loading && pending == null)
                      const KhadraLoading()
                    else if (_loadFailure != null && pending == null) ...[
                      KhadraNotice(
                        title: _loadFailure!.messageFor(l10n),
                        tone: NoticeTone.warn,
                        icon: Icons.info_outline,
                      ),
                      const SizedBox(height: Space.md),
                      FilledButton(onPressed: busy ? null : _load, child: Text(l10n.actionRetry)),
                    ] else if (pending != null) ...[
                      for (final document in pending)
                        Padding(
                          padding: const EdgeInsets.only(bottom: Space.sm),
                          child: KhadraCard(
                            child: Row(
                              children: [
                                Expanded(
                                  child: Column(
                                    crossAxisAlignment: CrossAxisAlignment.start,
                                    children: [
                                      Text(
                                        legalDocumentTitle(l10n, document.kind),
                                        style: const TextStyle(fontWeight: FontWeight.w700),
                                      ),
                                      Text(
                                        l10n.legalVersion(document.versionLabel),
                                        style: const TextStyle(fontSize: 13, color: KhadraColors.neutral600),
                                      ),
                                    ],
                                  ),
                                ),
                                TextButton(
                                  key: ValueKey('consent-read-${document.kind}'),
                                  onPressed: busy
                                      ? null
                                      : () => Navigator.of(context).push(MaterialPageRoute<void>(
                                            builder: (_) =>
                                                LegalTextScreen(document: document, htmlView: widget.htmlView),
                                          )),
                                  child: Text(l10n.consentRead),
                                ),
                              ],
                            ),
                          ),
                        ),
                      const SizedBox(height: Space.sm),
                      CheckboxListTile(
                        key: const ValueKey('consent-agree'),
                        value: _agreed,
                        onChanged: busy
                            ? null
                            : (value) => setState(() {
                                  _agreed = value ?? false;
                                  if (_agreed) _notice = null;
                                }),
                        title: Text(l10n.consentAgree),
                        controlAffinity: ListTileControlAffinity.leading,
                        contentPadding: EdgeInsets.zero,
                      ),
                      if (_notice != null) ...[
                        const SizedBox(height: Space.sm),
                        KhadraNotice(title: _notice!, tone: NoticeTone.warn, icon: Icons.info_outline),
                      ],
                      const SizedBox(height: Space.lg),
                      FilledButton(
                        key: const ValueKey('consent-accept'),
                        onPressed: busy ? null : _accept,
                        child: Text(_submitting ? l10n.consentAccepting : l10n.consentAccept),
                      ),
                    ],
                    const SizedBox(height: Space.md),
                    TextButton(
                      key: const ValueKey('consent-sign-out'),
                      onPressed: busy ? null : _signOut,
                      child: Text(l10n.authSignOut),
                    ),
                  ],
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}
