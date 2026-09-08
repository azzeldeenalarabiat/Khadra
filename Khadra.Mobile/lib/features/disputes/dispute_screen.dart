import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:url_launcher/url_launcher.dart';

import '../../api/dtos.dart';
import '../../core/api/api_failure.dart';
import '../../core/api/api_failure_messages.dart';
import '../../core/format/formats.dart';
import '../../core/providers.dart';
import '../../core/router.dart';
import '../../core/theme/khadra_theme.dart';
import '../../core/widgets/khadra_widgets.dart';
import '../../l10n/app_localizations.dart';
import '../auth/auth_form_widgets.dart';
import '../bookings/booking_providers.dart';

/// One dispute: what was said, by whom, and what Khadra decided.
///
/// A resolution here is RECORDED, not executed. Nothing on this platform moves
/// money until the Payments context ships, and the screen says so beside the
/// figures rather than letting a customer read a refund as one that has happened.
class DisputeScreen extends ConsumerWidget {
  const DisputeScreen({super.key, required this.ticketId});

  final String ticketId;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);
    final dispute = ref.watch(disputeProvider(ticketId));
    final formats = ref.watch(formatsProvider);

    return Scaffold(
      appBar: AppBar(
        leading: const KhadraBack(fallback: Routes.bookings),
        title: Text(l10n.disputeViewTitle),
      ),
      body: RefreshIndicator(
        onRefresh: () => ref.refresh(disputeProvider(ticketId).future),
        child: switch (dispute) {
          AsyncLoading() => const KhadraLoading(),
          AsyncError(:final error) => KhadraError(
              message: ApiFailure.from(error).messageFor(l10n),
              onRetry: () => ref.invalidate(disputeProvider(ticketId)),
            ),
          AsyncData(:final value) when formats != null =>
            _Body(dispute: value, formats: formats),
          _ => const KhadraLoading(),
        },
      ),
    );
  }
}

class _Body extends ConsumerStatefulWidget {
  const _Body({required this.dispute, required this.formats});

  final Dispute dispute;
  final Formats formats;

  @override
  ConsumerState<_Body> createState() => _BodyState();
}

class _BodyState extends ConsumerState<_Body> {
  final _statement = TextEditingController();
  bool _busy = false;

  @override
  void dispose() {
    _statement.dispose();
    super.dispose();
  }

  Future<void> _addStatement() async {
    final l10n = AppLocalizations.of(context);
    final body = _statement.text.trim();
    if (body.isEmpty) return;

    setState(() => _busy = true);

    try {
      await ref.read(apiProvider).addDisputeStatement(
            ticketId: widget.dispute.ticketId,
            body: body,
          );
      _statement.clear();
      ref.invalidate(disputeProvider(widget.dispute.ticketId));
    } on ApiFailure catch (failure) {
      if (mounted) {
        showKhadraMessage(context, failure.messageFor(l10n), isError: true);
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _withdraw() async {
    final l10n = AppLocalizations.of(context);
    final confirmed = await showDialog<bool>(
      context: context,
      // The DIALOG's context. See the same note in ProfileScreen._signOut:
      // popping with the screen's context dismisses the SCREEN, not the dialog.
      builder: (dialogContext) => AlertDialog(
        title: Text(l10n.disputeWithdraw),
        content: Text(l10n.disputeWithdrawConfirm),
        actions: [
          TextButton(
            onPressed: () => Navigator.of(dialogContext).pop(false),
            child: Text(l10n.actionCancel),
          ),
          FilledButton(
            onPressed: () => Navigator.of(dialogContext).pop(true),
            child: Text(l10n.disputeWithdraw),
          ),
        ],
      ),
    );

    if (confirmed != true) return;

    try {
      await ref.read(apiProvider).withdrawDispute(widget.dispute.ticketId);
      ref.invalidate(disputeProvider(widget.dispute.ticketId));
      invalidateBookings(ref, bookingId: widget.dispute.bookingId);
      if (mounted) showKhadraMessage(context, l10n.disputeWithdrawn);
    } on ApiFailure catch (failure) {
      if (mounted) {
        showKhadraMessage(context, failure.messageFor(l10n), isError: true);
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final dispute = widget.dispute;
    final formats = widget.formats;

    return ListView(
      padding: const EdgeInsets.fromLTRB(
          Space.lg, Space.lg, Space.lg, Space.bottomInset),
      children: [
        KhadraCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  KhadraBadge(
                    label: _statusLabel(l10n, dispute.status),
                    colour: _statusColour(dispute.status),
                  ),
                  const Spacer(),
                  Text(
                    formats.longDate(dispute.openedAt),
                    style: const TextStyle(
                        color: KhadraColors.neutral500, fontSize: 12),
                  ),
                ],
              ),
              const SizedBox(height: Space.md),
              Text(
                dispute.reason,
                style: const TextStyle(fontSize: 14, height: 1.55),
              ),
            ],
          ),
        ),

        if (dispute.resolution case final resolution?) ...[
          const SizedBox(height: Space.lg),
          _Resolution(resolution: resolution, formats: formats),
        ],

        const SizedBox(height: Space.xl),
        KhadraSectionTitle(l10n.disputeStatements),
        for (final statement in dispute.statements) ...[
          _Statement(statement: statement, formats: formats),
          const SizedBox(height: Space.md),
        ],

        if (dispute.isLive) ...[
          const SizedBox(height: Space.lg),
          KhadraField(
            controller: _statement,
            label: l10n.disputeAddStatement,
            hint: l10n.disputeStatementHint,
            maxLines: 4,
            maxLength: 4000,
            enabled: !_busy,
          ),
          KhadraSubmitButton(
            label: l10n.actionSubmit,
            busy: _busy,
            onPressed: _addStatement,
          ),

          // Only the party who OPENED it may take it back; the server refuses
          // anyone else, and offering the button to the other side would be a
          // control that exists to be denied.
          if (dispute.openedByMe) ...[
            const SizedBox(height: Space.lg),
            OutlinedButton.icon(
              onPressed: _busy ? null : _withdraw,
              icon: const Icon(Icons.undo, size: 18),
              label: Text(l10n.disputeWithdraw),
            ),
          ],
        ],
      ],
    );
  }

  static String _statusLabel(AppLocalizations l10n, String status) =>
      switch (status) {
        'Open' => l10n.disputeStatusOpen,
        'UnderReview' => l10n.disputeStatusUnderReview,
        'Resolved' => l10n.disputeStatusResolved,
        'Withdrawn' => l10n.disputeStatusWithdrawn,
        _ => status,
      };

  static Color _statusColour(String status) => switch (status) {
        'Open' || 'UnderReview' => KhadraColors.warn,
        'Resolved' => KhadraColors.accent,
        _ => KhadraColors.neutral600,
      };
}

class _Statement extends StatelessWidget {
  const _Statement({required this.statement, required this.formats});

  final DisputeStatement statement;
  final Formats formats;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final mine = statement.party == 'Customer';

    return KhadraCard(
      background: mine ? KhadraColors.accent100 : KhadraColors.surface,
      borderColor: mine ? KhadraColors.accent200 : KhadraColors.neutral200,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Text(
                _party(l10n, statement.party),
                style: const TextStyle(
                    fontSize: 13, fontWeight: FontWeight.w700),
              ),
              const Spacer(),
              Text(
                formats.dateTime(statement.createdAt),
                style: const TextStyle(
                    color: KhadraColors.neutral500, fontSize: 11),
              ),
            ],
          ),
          const SizedBox(height: Space.sm),
          Text(
            statement.body,
            style: const TextStyle(fontSize: 14, height: 1.5),
          ),
          if (statement.evidence.isNotEmpty) ...[
            const SizedBox(height: Space.sm),
            Wrap(
              spacing: Space.sm,
              runSpacing: Space.sm,
              children: [
                for (final (index, link) in statement.evidence.indexed)
                  ActionChip(
                    avatar: const Icon(Icons.attachment, size: 16),
                    // NUMBERED, not named. The server derives the name from the
                    // storage key it generated, so what arrives is a hash --
                    // "35f1168dd02b4a73893dcc2ea113bb64.jpg" -- and the name the
                    // customer's own file had was never kept. A number is at least
                    // something they can point at.
                    label: Text(
                      l10n.disputeEvidenceNumbered(index + 1),
                      style: const TextStyle(fontSize: 12),
                    ),
                    // Opened externally: the URL is signed and short-lived, and
                    // caching it in the app would outlive the permission it
                    // carries.
                    onPressed: () => launchUrl(
                      Uri.parse(link.url),
                      mode: LaunchMode.externalApplication,
                    ),
                  ),
              ],
            ),
          ],
        ],
      ),
    );
  }

  static String _party(AppLocalizations l10n, String party) => switch (party) {
        'Customer' => l10n.disputeYou,
        'Dealer' => l10n.disputeGallery,
        'Admin' => l10n.disputeKhadra,
        _ => party,
      };
}

/// The decision, as money.
///
/// Every figure is the server's split of the deposit it actually holds. The line
/// saying nothing has moved is not decoration: until Payments exists, a resolution
/// is a record of a decision and no funds change hands.
class _Resolution extends StatelessWidget {
  const _Resolution({required this.resolution, required this.formats});

  final DisputeResolution resolution;
  final Formats formats;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);

    return KhadraCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            l10n.disputeStatusResolved,
            style: const TextStyle(fontSize: 16, fontWeight: FontWeight.w700),
          ),
          const SizedBox(height: Space.md),
          KhadraDetailRow(
            label: l10n.vehicleSecurityDeposit,
            value: Text(formats.money(resolution.depositHeld)),
          ),
          KhadraDetailRow(
            label: l10n.bookingPartyCustomer,
            value: Text(formats.money(resolution.refundToCustomer)),
          ),
          KhadraDetailRow(
            label: l10n.bookingPartyDealer,
            value: Text(formats.money(resolution.transferredToDealer)),
          ),
          KhadraDetailRow(
            label: l10n.bookingPartyAdmin,
            value: Text(formats.money(resolution.retainedByPlatform)),
          ),
          if (resolution.note.isNotEmpty) ...[
            const SizedBox(height: Space.sm),
            Text(
              resolution.note,
              style: const TextStyle(fontSize: 13, height: 1.5),
            ),
          ],
          const SizedBox(height: Space.md),
          KhadraNotice(
            title: l10n.bookingPenaltyNotCharged,
            tone: NoticeTone.neutral,
          ),
        ],
      ),
    );
  }
}
