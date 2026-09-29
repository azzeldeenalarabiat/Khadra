import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:url_launcher/url_launcher.dart';

import '../../core/api/api_failure.dart';
import '../../core/api/api_failure_messages.dart';
import '../../core/format/formats.dart';
import '../../core/providers.dart';
import '../../core/router.dart';
import '../../core/theme/khadra_theme.dart';
import '../../core/widgets/khadra_widgets.dart';
import '../../l10n/app_localizations.dart';
import 'document_content.dart';
import 'invoice_content.dart';
import 'invoice_presentation.dart';
import 'invoice_providers.dart';

/// One issued financial document (payments Phase 5b), at
/// `/profile/invoices/:documentId` — an address Phase 7 will email, so it never
/// moves. The stored document in the app's language; around it, its standing, a
/// void or a newer version, its other versions and the receipts it belongs with;
/// and its PDFs (Phase 6), one per language drawn.
///
/// A document that is not the reader's, and one that does not exist, read exactly
/// alike: nothing here says whether it exists.
class InvoiceScreen extends ConsumerWidget {
  const InvoiceScreen({super.key, required this.documentId});

  final String documentId;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);
    final formats = ref.watch(formatsProvider);

    // A document this build refused whole is reported for support: its id and
    // schema version only, never the snapshot, which holds a customer's name and
    // their money (docs/contracts/README.md).
    ref.listen(financialDocumentProvider(documentId), (_, next) {
      final page = next.valueOrNull;
      if (kDebugMode && page != null && DocumentContent.tryParse(page.snapshotSchemaVersion, page.snapshot) == null) {
        debugPrint('A financial document could not be shown whole: ${page.row.documentId} '
            '(schema ${page.snapshotSchemaVersion})');
      }
    });

    return Scaffold(
      appBar: AppBar(
        leading: const KhadraBack(fallback: Routes.invoices),
        title: Text(l10n.invoicesTitle),
      ),
      body: switch (ref.watch(financialDocumentProvider(documentId))) {
        AsyncError(:final error) => KhadraError(
            message: ApiFailure.from(error).messageFor(l10n),
            onRetry: () => ref.invalidate(financialDocumentProvider(documentId)),
          ),
        // In a list, as the app's other empty states are, so a short screen — a
        // phone on its side, or large text — scrolls to the way back rather than
        // overflowing over it.
        AsyncData(:final value) when value == null => ListView(
            children: [
              KhadraEmpty(
                icon: Icons.receipt_long_outlined,
                title: l10n.invoicesNotAvailable,
                action: TextButton(
                  onPressed: () => context.go(Routes.invoices),
                  child: Text(l10n.invoicesBackToList),
                ),
              ),
            ],
          ),
        AsyncData(:final value) when formats != null => RefreshIndicator(
            onRefresh: () async {
              ref.invalidate(financialDocumentProvider(documentId));
              await ref.read(financialDocumentProvider(documentId).future);
            },
            child: _Document(documentId: documentId, view: invoicePage(value!, l10n, formats), formats: formats),
          ),
        _ => const KhadraLoading(),
      },
    );
  }
}

class _Document extends ConsumerWidget {
  const _Document({required this.documentId, required this.view, required this.formats});

  final String documentId;
  final InvoicePageView view;
  final Formats formats;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);
    void open(String id) => context.push(Routes.invoice(id));

    return ListView(
      padding: const EdgeInsets.fromLTRB(Space.lg, Space.md, Space.lg, Space.bottomInset),
      children: [
        Text(
          view.title,
          style: TextStyle(
            fontSize: 22,
            fontWeight: FontWeight.w800,
            color: KhadraColors.text,
            letterSpacing: KhadraType.of(context, -0.4),
          ),
        ),
        const SizedBox(height: Space.xs),
        Wrap(
          spacing: Space.sm,
          runSpacing: Space.xs,
          crossAxisAlignment: WrapCrossAlignment.center,
          children: [
            LatinRun(view.number, style: const TextStyle(color: KhadraColors.neutral700, fontSize: 13, fontWeight: FontWeight.w700)),
            if (view.standing case final standing?) StandingBadge(standing: standing),
            Text(view.versionOf, style: const TextStyle(color: KhadraColors.neutral600, fontSize: 12)),
          ],
        ),
        Align(
          alignment: AlignmentDirectional.centerStart,
          child: TextButton(
            style: TextButton.styleFrom(padding: EdgeInsets.zero, visualDensity: VisualDensity.compact),
            onPressed: () => context.push(Routes.booking(view.bookingId)),
            child: Text(view.booking),
          ),
        ),
        if (view.pdf.opens.isNotEmpty || view.pdf.preparing) _PdfActions(documentId: documentId, pdf: view.pdf),
        if (view.voided case final voided?) ...[
          const SizedBox(height: Space.sm),
          KhadraNotice(
            title: voided.text,
            tone: NoticeTone.bad,
            action: voided.replacement == null
                ? null
                : TextButton(
                    onPressed: () => open(voided.replacement!.id),
                    child: NumberedSentence(voided.replacement!),
                  ),
          ),
        ],
        if (view.newer case final newer?) ...[
          const SizedBox(height: Space.sm),
          // The sentence is split between the notice and its button — "A newer
          // version exists:" above, the number to tap below — so the number is
          // said once and never broken across lines inside the title.
          KhadraNotice(
            title: newer.aroundNumber?.before ?? newer.text,
            tone: NoticeTone.warn,
            action: TextButton(
              onPressed: () => open(newer.id),
              child: NumberRun(number: newer.number, after: newer.aroundNumber?.after ?? ''),
            ),
          ),
        ],
        const SizedBox(height: Space.md),
        if (view.body case final body?) InvoiceContent(body: body) else _CannotShow(view: view),
        if (view.paymentReceipt != null || view.refundReceipts.isNotEmpty || view.versions.isNotEmpty) ...[
          const SizedBox(height: Space.md),
          KhadraCard(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                if (view.paymentReceipt case final receipt?) _LinkRow(sentence: receipt, onTap: () => open(receipt.id)),
                if (view.refundReceipts.isNotEmpty) ...[
                  _Heading(l10n.invoicesRefundsFromPayment),
                  for (final link in view.refundReceipts) _LinkRow(text: link.number, latin: true, standing: link.standing, onTap: () => open(link.id)),
                ],
                if (view.versions.isNotEmpty) ...[
                  _Heading(l10n.invoicesVersions),
                  for (final link in view.versions)
                    _LinkRow(
                      text: link.number,
                      detail: link.detail,
                      latin: true,
                      standing: link.standing,
                      onTap: link.here ? null : () => open(link.id),
                    ),
                ],
              ],
            ),
          ),
        ],
      ],
    );
  }
}

/// The document's PDFs (payments Phase 6): a button per language drawn, and a line
/// while one is still being drawn. A tap mints a link — it lasts minutes — fetches
/// the bytes over this app's authenticated connection, as the identity documents
/// are fetched, and hands them to the platform's viewer from the app's private
/// cache: never a browser, which has no session and would be refused.
class _PdfActions extends ConsumerStatefulWidget {
  const _PdfActions({required this.documentId, required this.pdf});

  final String documentId;
  final PdfView pdf;

  @override
  ConsumerState<_PdfActions> createState() => _PdfActionsState();
}

class _PdfActionsState extends ConsumerState<_PdfActions> {
  /// The language being fetched, while one is.
  String? _opening;

  Future<void> _open(PdfOpenView pdf) async {
    final l10n = AppLocalizations.of(context);
    setState(() => _opening = pdf.language);
    try {
      final api = ref.read(apiProvider);
      final link = await api.financialDocumentPdfLink(widget.documentId, pdf.language);
      final fetched = await api.documentBytes(link.url);
      final opened = await ref.read(fileOpenerProvider)(
        bytes: fetched.bytes,
        contentType: fetched.contentType,
        documentId: pdf.fileStem,
      );
      if (!mounted) return;
      if (!opened) showKhadraMessage(context, l10n.documentsOpenFailed, isError: true);
    } on ApiFailure catch (failure) {
      if (!mounted) return;
      showKhadraMessage(context, failure.messageFor(l10n), isError: true);
    } on Exception {
      // Writing the cache file, or the platform refusing to open it: words, not a crash.
      if (!mounted) return;
      showKhadraMessage(context, l10n.documentsOpenFailed, isError: true);
    } finally {
      if (mounted) setState(() => _opening = null);
    }
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return Padding(
      padding: const EdgeInsets.only(bottom: Space.xs),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          if (widget.pdf.opens.isNotEmpty)
            Wrap(
              spacing: Space.sm,
              runSpacing: Space.xs,
              children: [
                for (final pdf in widget.pdf.opens)
                  // The visible label stays the button's name; the fuller sentence is its tooltip.
                  Tooltip(
                    message: pdf.semantics,
                    child: OutlinedButton.icon(
                      onPressed: _opening == null ? () => _open(pdf) : null,
                      icon: _opening == pdf.language
                          ? const SizedBox.square(dimension: 16, child: CircularProgressIndicator(strokeWidth: 2))
                          : const Icon(Icons.picture_as_pdf_outlined, size: 18),
                      label: Text(pdf.label),
                    ),
                  ),
              ],
            ),
          if (widget.pdf.preparing)
            Padding(
              padding: const EdgeInsets.only(top: Space.xs),
              child: Text(l10n.invoicesPdfPreparing, style: const TextStyle(color: KhadraColors.neutral600, fontSize: 12)),
            ),
        ],
      ),
    );
  }
}

/// What the screen still states when this build cannot show a document whole: its
/// figure and when it was issued, and the way to a build that can.
class _CannotShow extends ConsumerWidget {
  const _CannotShow({required this.view});

  final InvoicePageView view;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);
    final link = ref.watch(appConfigProvider).valueOrNull?.mobileApp.updateUrl;

    return KhadraCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text(view.headlineLabel, style: const TextStyle(color: KhadraColors.neutral600, fontSize: 13)),
          Text(
            view.headline,
            style: const TextStyle(color: KhadraColors.price, fontSize: 18, fontWeight: FontWeight.w800),
          ),
          if (view.issued.isNotEmpty)
            Padding(
              padding: const EdgeInsets.only(top: Space.xs),
              child: Text(view.issued, style: const TextStyle(color: KhadraColors.neutral600, fontSize: 12)),
            ),
          const SizedBox(height: Space.md),
          KhadraNotice(
            title: l10n.invoicesCannotShowApp,
            tone: NoticeTone.warn,
            // A link when one is published, the sentence alone when not — the
            // update screen's own rule.
            action: link == null
                ? null
                : TextButton(
                    onPressed: () => launchUrl(link, mode: LaunchMode.externalApplication),
                    child: Text(l10n.updateRequiredAction),
                  ),
          ),
        ],
      ),
    );
  }
}

class _Heading extends StatelessWidget {
  const _Heading(this.text);

  final String text;

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.only(top: Space.sm, bottom: Space.xs),
        child: Text(text, style: const TextStyle(fontSize: 13, fontWeight: FontWeight.w800, color: KhadraColors.text)),
      );
}

class _LinkRow extends StatelessWidget {
  const _LinkRow({this.text = '', this.sentence, required this.onTap, this.detail = '', this.latin = false, this.standing});

  final String text;

  /// A sentence naming a number, shown in place of [text] with the number kept whole.
  final LinkText? sentence;
  final String detail;
  final bool latin;
  final StandingView? standing;

  /// Null for the document on screen: listed, not linked.
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final style = TextStyle(
      fontSize: 13,
      fontWeight: FontWeight.w700,
      color: onTap == null ? KhadraColors.text : KhadraColors.accent,
    );
    return InkWell(
      onTap: onTap,
      child: Padding(
        padding: const EdgeInsets.symmetric(vertical: Space.sm),
        child: Row(
          children: [
            Expanded(
              child: Wrap(
                spacing: Space.sm,
                runSpacing: 2,
                crossAxisAlignment: WrapCrossAlignment.center,
                children: [
                  if (sentence case final sentence?)
                    NumberedSentence(sentence, style: style)
                  else if (latin)
                    LatinRun(text, style: style)
                  else
                    Text(text, style: style),
                  if (detail.isNotEmpty) Text(detail, style: const TextStyle(color: KhadraColors.neutral600, fontSize: 12)),
                  if (standing case final chip?) StandingBadge(standing: chip),
                ],
              ),
            ),
            if (onTap != null) const KhadraDisclosure(),
          ],
        ),
      ),
    );
  }
}
