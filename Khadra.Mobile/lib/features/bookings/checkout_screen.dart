import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:webview_flutter/webview_flutter.dart';

import '../../api/dtos.dart';
import '../../core/format/formats.dart';
import '../../core/providers.dart';
import '../../core/theme/khadra_theme.dart';
import '../../core/widgets/khadra_widgets.dart';
import '../../l10n/app_localizations.dart';

/// How a checkout screen was left.
enum CheckoutExit {
  /// The customer closed it. Nothing is implied: the attempt may still be open, and paying again
  /// resumes it on the server.
  closed,

  /// The SERVER stopped treating this attempt as the one in flight — the booking was confirmed, or
  /// the attempt ended some other way. The booking screen re-reads and shows whichever it was.
  settled,
}

/// The provider's checkout page, inside the app.
///
/// **Provider-agnostic by construction.** It loads the URL the server minted for this attempt and
/// reads nothing back from it: no return URL is parsed, no page text is scraped, no JavaScript
/// channel is offered. That is what lets the same screen host the sandbox console today and a
/// HyperPay (or any hosted) checkout later with no change here — a provider's redirect back to
/// its result URL is just another page load this screen ignores.
///
/// **The outcome comes from the booking, never from the page.** Only a signed provider event
/// confirms a payment on this platform, so while the page is open this re-reads the booking and
/// closes itself the moment the server stops reporting THIS attempt as the live one
/// ([checkoutSettled]). The customer can always close it by hand; that cancels nothing, because
/// the session lives on the server until it expires and "Pay" resumes it.
///
/// **Why its own timer and not a `LiveSurface`.** The refresh policy's 20-second floor exists for
/// surfaces somebody glances at. A customer who has just pressed Pay is waiting on this one, so it
/// reads every [fastPoll] for the first [fastPhase] — when a webhook actually lands — and every
/// [slowPoll] after that, stops at the attempt's own expiry, and never reads while the app is not in
/// front of anybody (the same token-rotation reason the policy gives). It lives and dies with this
/// screen. These are mechanics of a waiting screen, not business rules.
class CheckoutScreen extends ConsumerStatefulWidget {
  const CheckoutScreen({
    super.key,
    required this.bookingId,
    required this.attempt,
    @visibleForTesting this.pageBuilder,
  });

  final String bookingId;
  final PaymentAttempt attempt;

  /// Replaces the WebView in widget tests, where no platform view exists.
  final Widget Function(Uri url)? pageBuilder;

  static const Duration fastPoll = Duration(seconds: 3);
  static const Duration fastPhase = Duration(seconds: 60);
  static const Duration slowPoll = Duration(seconds: 10);

  /// Opens the checkout over the current screen and answers how it was left.
  static Future<CheckoutExit> open(
    BuildContext context, {
    required String bookingId,
    required PaymentAttempt attempt,
  }) async =>
      await Navigator.of(context, rootNavigator: true).push<CheckoutExit>(
        MaterialPageRoute(
          fullscreenDialog: true,
          builder: (_) => CheckoutScreen(bookingId: bookingId, attempt: attempt),
        ),
      ) ??
      CheckoutExit.closed;

  @override
  ConsumerState<CheckoutScreen> createState() => _CheckoutScreenState();
}

/// Whether the server has finished with [paymentId] as far as this screen is concerned.
///
/// True when the booking no longer awaits a deposit (confirmed, expired, cancelled — whatever it
/// became), or when the attempt the server reports as live is no longer this one: a failed,
/// declined or expired attempt is not "in flight", so the server stops naming it.
///
/// Deliberately says nothing about WHICH of those happened. The booking screen reads the booking
/// and shows the server's own account of it; this only decides that waiting is over.
bool checkoutSettled(Booking booking, String paymentId) {
  if (!booking.isAwaitingPayment) return true;
  return booking.payment?.liveAttempt?.paymentId != paymentId;
}

/// What to tell the customer back on the booking screen, or null for nothing.
///
/// Every sentence is about what the SERVER now says, read after the page closed:
///
/// - Confirmed, or no longer awaiting a deposit for any reason: nothing. The booking screen itself
///   now shows the new status, and a toast repeating it would be a second, weaker source.
/// - Still awaiting payment, and [opened] is still the live attempt: the customer left a session
///   that is still open. Say until when, from the attempt's own expiry — and not that anything was
///   cancelled, because nothing was.
/// - Still awaiting payment, and [opened] is no longer live: that attempt ended without confirming
///   the booking. NOT "declined": the booking read does not say why an attempt ended (a decline, a
///   lapsed session and a provider failure all look the same from here), and the app claims only
///   what it was told.
String? checkoutReturnMessage(
  AppLocalizations l10n,
  CheckoutExit exit,
  Booking booking,
  PaymentAttempt opened,
  Formats? formats,
) {
  if (!booking.isAwaitingPayment) return null;

  final live = booking.payment?.liveAttempt;
  if (live != null && live.paymentId == opened.paymentId) {
    return formats == null ? null : l10n.checkoutStillOpen(formats.time(live.expiresAt));
  }
  return l10n.checkoutAttemptEnded;
}

/// How long to wait before the next read, [elapsed] after the page opened.
Duration checkoutPollInterval(Duration elapsed) =>
    elapsed < CheckoutScreen.fastPhase ? CheckoutScreen.fastPoll : CheckoutScreen.slowPoll;

/// Whether the WebView may follow a navigation to [uri].
///
/// Web content only. A hosted checkout moves between the provider, a bank's 3-D Secure page and
/// back, all over https, and the screen must not care which host it is on; card widgets also build
/// frames from `about:blank`, `data:` and `blob:`, which are content, not destinations. What it
/// refuses is everything else — `intent:`, `market:`, `tel:`, `mailto:`, a custom scheme — because
/// each of those hands the customer to another app mid-payment, which is exactly what keeping
/// checkout in the app is for. Plain `http` is allowed here and still refused by the release
/// network security config, which is the layer that owns that rule.
bool allowCheckoutNavigation(Uri uri) =>
    const {'https', 'http', 'about', 'data', 'blob'}.contains(uri.scheme.toLowerCase());

class _CheckoutScreenState extends ConsumerState<CheckoutScreen> with WidgetsBindingObserver {
  WebViewController? _controller;
  final Stopwatch _open = Stopwatch()..start();
  Timer? _next;
  bool _reading = false;
  bool _done = false;
  bool _resumed = true;

  int _progress = 0;
  bool _loadFailed = false;
  late final Uri _url = Uri.parse(widget.attempt.checkoutUrl!);

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    if (widget.pageBuilder == null) _controller = _buildController();
    _schedule();
  }

  WebViewController _buildController() => WebViewController()
    ..setJavaScriptMode(JavaScriptMode.unrestricted)
    ..setNavigationDelegate(NavigationDelegate(
      onNavigationRequest: (request) {
        final uri = Uri.tryParse(request.url);
        return uri != null && allowCheckoutNavigation(uri)
            ? NavigationDecision.navigate
            : NavigationDecision.prevent;
      },
      onProgress: (value) {
        if (mounted) setState(() => _progress = value);
      },
      onPageStarted: (_) {
        if (mounted) setState(() => _loadFailed = false);
      },
      onWebResourceError: (error) {
        // Only the page itself. A missing favicon or a tracker the provider embeds is not a
        // failure to load the checkout.
        if (error.isForMainFrame ?? true) {
          if (mounted) setState(() => _loadFailed = true);
        }
      },
      // Every page load is also a moment a result may have landed — the provider's page usually
      // changes right after the customer submits. Reading then, rather than waiting out the
      // interval, is what makes the close feel immediate.
      onPageFinished: (_) => _readNow(),
    ))
    ..loadRequest(_url);

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    final resumed = state == AppLifecycleState.resumed;
    if (resumed == _resumed) return;
    _resumed = resumed;
    if (resumed) {
      _readNow();
    } else {
      _next?.cancel();
    }
  }

  void _schedule() {
    _next?.cancel();
    if (_done || !_resumed) return;
    // Past the attempt's own expiry there is nothing left to wait for: the session is dead on the
    // provider's side and the server's sweep will end it. The margin buys one last read after it.
    if (DateTime.now().toUtc().isAfter(widget.attempt.expiresAt.add(CheckoutScreen.slowPoll))) {
      return;
    }
    _next = Timer(checkoutPollInterval(_open.elapsed), _readNow);
  }

  Future<void> _readNow() async {
    if (_done || _reading || !_resumed) return;
    _reading = true;
    try {
      final booking = await ref.read(apiProvider).booking(widget.bookingId);
      if (!mounted || _done) return;
      if (checkoutSettled(booking, widget.attempt.paymentId)) {
        _finish(CheckoutExit.settled);
        return;
      }
    } on Object {
      // A read that failed is not an answer. Keep the page up and ask again next time; the
      // customer can always close it.
    } finally {
      _reading = false;
    }
    if (mounted) _schedule();
  }

  void _finish(CheckoutExit exit) {
    if (_done) return;
    _done = true;
    _next?.cancel();
    Navigator.of(context).pop(exit);
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    _next?.cancel();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);

    return PopScope(
      canPop: false,
      // Android Back closes the checkout rather than stepping back inside it. Walking backwards
      // through a card form and a 3-D Secure page resubmits things; leaving it does not, and Pay
      // brings the customer straight back to the same session.
      onPopInvokedWithResult: (didPop, _) {
        if (!didPop) _finish(CheckoutExit.closed);
      },
      child: Scaffold(
        appBar: AppBar(
          leading: IconButton(
            icon: const Icon(Icons.close),
            tooltip: l10n.checkoutClose,
            onPressed: () => _finish(CheckoutExit.closed),
          ),
          title: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(l10n.checkoutTitle),
              // Where the customer is about to type, from the URL itself. An in-app page has no
              // address bar, and this is the one fact about the page that is not the page's own
              // claim.
              Text(
                _url.host,
                style: const TextStyle(
                  fontSize: 12,
                  fontWeight: FontWeight.w500,
                  color: KhadraColors.neutral600,
                ),
              ),
            ],
          ),
          bottom: _progress > 0 && _progress < 100
              ? PreferredSize(
                  preferredSize: const Size.fromHeight(2),
                  child: LinearProgressIndicator(value: _progress / 100, minHeight: 2),
                )
              : null,
        ),
        body: _loadFailed
            ? Padding(
                padding: const EdgeInsets.all(Space.lg),
                child: Column(
                  mainAxisAlignment: MainAxisAlignment.center,
                  children: [
                    KhadraNotice(
                      title: l10n.checkoutLoadFailed,
                      tone: NoticeTone.warn,
                      icon: Icons.wifi_off_outlined,
                    ),
                    const SizedBox(height: Space.md),
                    FilledButton(
                      onPressed: () {
                        setState(() => _loadFailed = false);
                        _controller?.loadRequest(_url);
                      },
                      child: Text(l10n.actionRetry),
                    ),
                  ],
                ),
              )
            : widget.pageBuilder?.call(_url) ?? WebViewWidget(controller: _controller!),
      ),
    );
  }
}
