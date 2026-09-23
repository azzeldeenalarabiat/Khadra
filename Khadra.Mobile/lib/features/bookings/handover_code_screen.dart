import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:qr_flutter/qr_flutter.dart';

import '../../api/dtos.dart';
import '../../core/api/api_failure.dart';
import '../../core/api/api_failure_messages.dart';
import '../../core/providers.dart';
import '../../core/theme/khadra_theme.dart';
import '../../core/widgets/khadra_widgets.dart';
import '../../l10n/app_localizations.dart';

/// The code a customer shows at the counter, as six digits and as a QR.
///
/// **Asked for when the screen opens**, never cached: each opening mints a new code and the
/// previous one stops working, so a screenshot shared earlier is already dead. The server
/// decides whether it is for the pickup or the return.
///
/// **Closes itself when the handover is recorded.** While it is open it re-reads the booking
/// every [poll]; when the status moves on — the dealer typed or scanned the code — it pops
/// with `true` and the booking screen shows the new status. The customer presses nothing:
/// the code WAS their confirmation. A push, when one arrives, refreshes the same booking.
///
/// **Expiry is the server's figure.** The countdown runs to the `expiresAt` the server sent;
/// past it the code is shown as expired with a button for a new one.
class HandoverCodeScreen extends ConsumerStatefulWidget {
  const HandoverCodeScreen({super.key, required this.bookingId, required this.statusWhenOpened});

  final String bookingId;

  /// The booking's status when the customer opened this. The handover is done when it changes.
  final String statusWhenOpened;

  static const Duration poll = Duration(seconds: 5);

  /// Opens the screen; answers true when the handover was recorded while it was open.
  static Future<bool> open(BuildContext context, {required String bookingId, required String status}) async =>
      await Navigator.of(context, rootNavigator: true).push<bool>(
        MaterialPageRoute(
          fullscreenDialog: true,
          builder: (_) => HandoverCodeScreen(bookingId: bookingId, statusWhenOpened: status),
        ),
      ) ??
      false;

  @override
  ConsumerState<HandoverCodeScreen> createState() => _HandoverCodeScreenState();
}

class _HandoverCodeScreenState extends ConsumerState<HandoverCodeScreen> {
  HandoverCodeGrant? _grant;
  ApiFailure? _failure;
  bool _loading = true;
  Timer? _tick;
  Timer? _watch;
  bool _done = false;

  @override
  void initState() {
    super.initState();
    _issue();
    // Once a second for the countdown; the booking itself is read far less often.
    _tick = Timer.periodic(const Duration(seconds: 1), (_) {
      if (mounted) setState(() {});
    });
    _watch = Timer.periodic(HandoverCodeScreen.poll, (_) => _checkHandedOver());
  }

  Future<void> _issue() async {
    setState(() {
      _loading = true;
      _failure = null;
    });
    try {
      final grant = await ref.read(apiProvider).issueHandoverCode(widget.bookingId);
      if (mounted) setState(() => _grant = grant);
    } on ApiFailure catch (failure) {
      if (mounted) setState(() => _failure = failure);
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _checkHandedOver() async {
    // Not while the app is behind something else: every read may rotate the session, and the
    // refresh policy stops all polling when nobody is looking. Coming back reads on the next tick.
    final state = WidgetsBinding.instance.lifecycleState;
    if (_done || (state != null && state != AppLifecycleState.resumed)) return;
    try {
      final booking = await ref.read(apiProvider).booking(widget.bookingId);
      if (!mounted || _done) return;
      if (booking.status != widget.statusWhenOpened) {
        _done = true;
        Navigator.of(context).pop(true);
      }
    } on ApiFailure {
      // A read that failed is not an answer; the next tick asks again.
    }
  }

  @override
  void dispose() {
    _tick?.cancel();
    _watch?.cancel();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final grant = _grant;
    final expired = grant != null && !DateTime.now().toUtc().isBefore(grant.expiresAt);

    return Scaffold(
      appBar: AppBar(
        title: Text(grant?.isReturn == true ? l10n.handoverReturnTitle : l10n.handoverPickupTitle),
        leading: IconButton(
          icon: const Icon(Icons.close),
          tooltip: l10n.actionClose,
          onPressed: () => Navigator.of(context).pop(false),
        ),
      ),
      body: SafeArea(
        child: Center(
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: Space.measure),
            child: ListView(
              padding: const EdgeInsets.all(Space.xl),
              children: [
                if (_loading && grant == null)
                  const Padding(
                    padding: EdgeInsets.all(Space.xxl),
                    child: Center(child: CircularProgressIndicator()),
                  )
                else if (_failure != null && grant == null)
                  KhadraNotice(
                    title: _failure!.messageFor(l10n),
                    tone: NoticeTone.warn,
                    icon: Icons.error_outline,
                  )
                else if (grant != null) ...[
                  Text(
                    l10n.handoverInstructions,
                    textAlign: TextAlign.center,
                    style: const TextStyle(fontSize: 15, height: 1.45, color: KhadraColors.neutral700),
                  ),
                  const SizedBox(height: Space.xl),
                  Opacity(
                    opacity: expired ? 0.25 : 1,
                    child: Center(
                      child: QrImageView(
                        key: const ValueKey('handover-qr'),
                        data: grant.qrPayload,
                        size: 220,
                        backgroundColor: KhadraColors.surface,
                      ),
                    ),
                  ),
                  const SizedBox(height: Space.lg),
                  // Digits are Latin in both languages and never reordered: this is read aloud
                  // and typed into a console, so the order on screen is the order to type.
                  Center(
                    child: LatinRun(
                      '${grant.code.substring(0, 3)} ${grant.code.substring(3)}',
                      key: const ValueKey('handover-code'),
                      style: TextStyle(
                        fontSize: 44,
                        fontWeight: FontWeight.w800,
                        color: expired ? KhadraColors.neutral400 : KhadraColors.text,
                        fontFeatures: const [FontFeature.tabularFigures()],
                      ),
                    ),
                  ),
                  const SizedBox(height: Space.sm),
                  Text(
                    expired ? l10n.handoverExpired : l10n.handoverValidFor(_remaining(grant.expiresAt)),
                    textAlign: TextAlign.center,
                    style: TextStyle(
                      fontSize: 14,
                      fontWeight: FontWeight.w600,
                      color: expired ? KhadraColors.warn : KhadraColors.neutral600,
                    ),
                  ),
                  const SizedBox(height: Space.xl),
                  OutlinedButton.icon(
                    onPressed: _loading ? null : _issue,
                    icon: const Icon(Icons.refresh, size: 18),
                    label: Text(l10n.handoverNewCode),
                  ),
                  const SizedBox(height: Space.md),
                  Text(
                    l10n.handoverPrivacy,
                    textAlign: TextAlign.center,
                    style: const TextStyle(fontSize: 12, height: 1.4, color: KhadraColors.neutral600),
                  ),
                ],
              ],
            ),
          ),
        ),
      ),
    );
  }

  /// "14:59" — minutes and seconds left, from the server's own expiry.
  static String _remaining(DateTime expiresAt) {
    final left = expiresAt.difference(DateTime.now().toUtc());
    if (left.isNegative) return '0:00';
    final minutes = left.inMinutes;
    final seconds = left.inSeconds % 60;
    return '$minutes:${seconds.toString().padLeft(2, '0')}';
  }
}
