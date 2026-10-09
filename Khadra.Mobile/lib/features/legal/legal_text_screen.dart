import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:url_launcher/url_launcher.dart';
import 'package:webview_flutter/webview_flutter.dart';

import '../../api/dtos.dart';
import '../../core/api/api_failure.dart';
import '../../core/api/api_failure_messages.dart';
import '../../core/providers.dart';
import '../../core/theme/khadra_theme.dart';
import '../../core/widgets/khadra_widgets.dart';
import '../../l10n/app_localizations.dart';

/// Draws the server's rendered HTML. A WebView on a phone; replaced in tests, which
/// have no platform views.
typedef LegalHtmlView = Widget Function(BuildContext context, String html, {required bool arabic, Uri? base});

/// The name of a legal text in the reader's language.
String legalDocumentTitle(AppLocalizations l10n, String kind) =>
    kind == 'Terms' ? l10n.legalTerms : l10n.legalPrivacy;

/// One legal text in force, exactly as the server rendered it, and only ever the
/// VERSION a customer is reading in order to accept it (pre-launch items 224, 238).
///
/// The public read is cached for up to five minutes, so straight after a publish it
/// can still answer with the previous version while `/auth/me/legal-consents`
/// already names the new one. The screen compares the two ids and, when they
/// differ, says the text has just been updated rather than show a version the
/// customer is not agreeing to. Nothing here is the app's: the words are the
/// server's, and so is which version is in force.
class LegalTextScreen extends ConsumerStatefulWidget {
  const LegalTextScreen({super.key, required this.document, this.htmlView});

  /// The text and the version being read: from `/app-config` (registration,
  /// Profile) or from the consent prompt's uncached list.
  final LegalDocumentRef document;

  final LegalHtmlView? htmlView;

  static Future<void> open(BuildContext context, LegalDocumentRef document) =>
      Navigator.of(context).push(MaterialPageRoute<void>(builder: (_) => LegalTextScreen(document: document)));

  @override
  ConsumerState<LegalTextScreen> createState() => _LegalTextScreenState();
}

class _LegalTextScreenState extends ConsumerState<LegalTextScreen> {
  PublicLegalDocument? _text;
  ApiFailure? _failure;
  bool _loading = true;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _failure = null;
    });
    try {
      final text = await ref.read(apiProvider).legalDocument(widget.document.slug);
      if (mounted) setState(() => _text = text);
    } on ApiFailure catch (failure) {
      if (mounted) setState(() => _failure = failure);
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final arabic = ref.watch(isArabicProvider);
    final text = _text;
    final current = text != null && text.versionId == widget.document.versionId;

    return Scaffold(
      appBar: AppBar(title: Text(legalDocumentTitle(l10n, widget.document.kind))),
      body: SafeArea(
        child: _loading && text == null
            ? const KhadraLoading()
            : _failure != null || (text != null && !current)
                ? ListView(
                    padding: const EdgeInsets.all(Space.xl),
                    children: [
                      KhadraNotice(
                        title: _failure?.messageFor(l10n) ?? l10n.legalTextJustUpdated,
                        tone: NoticeTone.warn,
                        icon: Icons.info_outline,
                      ),
                      const SizedBox(height: Space.md),
                      FilledButton(onPressed: _loading ? null : _load, child: Text(l10n.actionRetry)),
                    ],
                  )
                : Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      Padding(
                        padding: const EdgeInsets.fromLTRB(Space.xl, Space.md, Space.xl, Space.sm),
                        child: Text(
                          l10n.legalVersion(text!.versionLabel),
                          style: const TextStyle(fontSize: 13, color: KhadraColors.neutral600),
                        ),
                      ),
                      Expanded(child: _html(context, text.htmlFor(arabic: arabic), arabic)),
                    ],
                  ),
      ),
    );
  }

  Widget _html(BuildContext context, String html, bool arabic) {
    final page = widget.document.pageUrls?.forLanguage(arabic: arabic);
    final base = page == null ? null : Uri.tryParse(page);
    final custom = widget.htmlView;
    if (custom != null) return custom(context, html, arabic: arabic, base: base);
    // The web build has no WebView: it opens the published page instead. A phone always
    // reads the text in place.
    if (kIsWeb) {
      final l10n = AppLocalizations.of(context);
      return Center(
        child: base == null
            ? Text(l10n.legalTextJustUpdated)
            : FilledButton(
                onPressed: () => launchUrl(base, mode: LaunchMode.externalApplication),
                child: Text(l10n.legalTextOpenPage),
              ),
      );
    }
    return _LegalWebView(html: html, arabic: arabic, base: base);
  }
}

/// Whether a tap inside a legal text may leave the app: the server publishes links to
/// `https:`, `mailto:` and pages of the website only, and each opens outside the app.
bool legalLinkOpensOutside(Uri uri) => const {'https', 'mailto'}.contains(uri.scheme.toLowerCase());

class _LegalWebView extends StatefulWidget {
  const _LegalWebView({required this.html, required this.arabic, required this.base});

  final String html;
  final bool arabic;
  final Uri? base;

  @override
  State<_LegalWebView> createState() => _LegalWebViewState();
}

class _LegalWebViewState extends State<_LegalWebView> {
  /// Whether the text below has loaded. Until then a navigation is the text itself: iOS asks
  /// about the initial `loadHtmlString` under the base URL, an `https:` address, which must
  /// not be sent to the browser. Android does not ask. After it, every tap is a link.
  bool _loaded = false;

  late final WebViewController _controller = WebViewController()
    // Static text: nothing in it needs script, and none is let in.
    ..setJavaScriptMode(JavaScriptMode.disabled)
    ..setNavigationDelegate(NavigationDelegate(
      onPageFinished: (_) => _loaded = true,
      onNavigationRequest: (request) {
        if (!_loaded) return NavigationDecision.navigate;
        final uri = Uri.tryParse(request.url);
        // A link never loads inside the text: it opens outside the app, or nowhere.
        if (uri != null && legalLinkOpensOutside(uri)) launchUrl(uri, mode: LaunchMode.externalApplication);
        return NavigationDecision.prevent;
      },
    ))
    ..loadHtmlString(_page(widget.html, widget.arabic), baseUrl: widget.base?.toString());

  /// The fragment the server rendered, set in the reader's direction and language, as the
  /// renderer's contract asks of its clients. Its colours are the theme's tokens, written out
  /// as CSS, so the page is the app's and no colour is named outside `khadra_theme.dart`.
  static String _page(String html, bool arabic) => '''
<!doctype html>
<html lang="${arabic ? 'ar' : 'en'}" dir="${arabic ? 'rtl' : 'ltr'}">
<head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<style>
body { font-family: ${arabic ? "'Noto Kufi Arabic', " : ''}'Manrope', system-ui, sans-serif; font-size: 15px; line-height: 1.6;
  color: ${_css(KhadraColors.text)}; background: ${_css(KhadraColors.surface)}; margin: 16px 20px 32px; overflow-wrap: anywhere; }
h1, h2, h3, h4 { line-height: 1.3; margin: 1.2em 0 0.4em; }
a { color: ${_css(KhadraColors.accent)}; }
blockquote { margin: 0; padding-inline-start: 12px; border-inline-start: 3px solid ${_css(KhadraColors.neutral300)};
  color: ${_css(KhadraColors.neutral700)}; }
</style></head>
<body>$html</body></html>''';

  static String _css(Color color) =>
      '#${(color.toARGB32() & 0xFFFFFF).toRadixString(16).padLeft(6, '0')}';

  @override
  Widget build(BuildContext context) => WebViewWidget(controller: _controller);
}
