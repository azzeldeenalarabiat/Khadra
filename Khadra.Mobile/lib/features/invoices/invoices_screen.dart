import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../api/dtos.dart';
import '../../core/api/api_failure.dart';
import '../../core/api/api_failure_messages.dart';
import '../../core/paging.dart';
import '../../core/providers.dart';
import '../../core/router.dart';
import '../../core/theme/khadra_theme.dart';
import '../../core/widgets/khadra_widgets.dart';
import '../../l10n/app_localizations.dart';
import 'invoice_content.dart';
import 'invoice_presentation.dart';
import 'invoice_providers.dart';

/// Invoices & Receipts (payments Phase 5b; owner, 2026-09-27), entered from My
/// Account — not a tab: the bar keeps its five destinations. Every issued
/// document of the customer's, newest issued first, every version marked, a page
/// at a time as the list is scrolled.
class InvoicesScreen extends ConsumerStatefulWidget {
  const InvoicesScreen({super.key});

  @override
  ConsumerState<InvoicesScreen> createState() => _InvoicesScreenState();
}

class _InvoicesScreenState extends ConsumerState<InvoicesScreen> {
  final _scrollController = ScrollController();
  late final EndOfListLoader _loader = EndOfListLoader(
    controller: _scrollController,
    onReachEnd: () => unawaited(_loadMore()),
  );
  String _filter = InvoiceFilters.all;

  @override
  void initState() {
    super.initState();
    _loader; // Attaches the listener.
  }

  @override
  void dispose() {
    _loader.dispose();
    _scrollController.dispose();
    super.dispose();
  }

  Future<void> _loadMore() async {
    try {
      await ref.read(myFinancialDocumentsProvider(_filter).notifier).loadMore();
    } on ApiFailure catch (failure) {
      if (!mounted) return;
      showKhadraMessage(context, failure.messageFor(AppLocalizations.of(context)), isError: true);
    }
  }

  String _label(AppLocalizations l10n, String filter) => switch (filter) {
        FinancialDocumentTypes.paymentReceipt => l10n.invoicesFilterPaymentReceipt,
        FinancialDocumentTypes.refundReceipt => l10n.invoicesFilterRefundReceipt,
        FinancialDocumentTypes.bookingStatement => l10n.invoicesFilterBookingStatement,
        _ => l10n.invoicesFilterAll,
      };

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final formats = ref.watch(formatsProvider);
    final documents = ref.watch(myFinancialDocumentsProvider(_filter));

    return Scaffold(
      appBar: AppBar(
        leading: const KhadraBack(fallback: Routes.profile),
        title: Text(l10n.invoicesTitle),
      ),
      body: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          SingleChildScrollView(
            scrollDirection: Axis.horizontal,
            padding: const EdgeInsets.symmetric(horizontal: Space.lg, vertical: Space.sm),
            child: Row(
              children: [
                for (final filter in InvoiceFilters.ordered)
                  Padding(
                    padding: const EdgeInsetsDirectional.only(end: Space.sm),
                    child: KhadraChoiceChip(
                      label: _label(l10n, filter),
                      selected: filter == _filter,
                      onTap: () => setState(() => _filter = filter),
                    ),
                  ),
              ],
            ),
          ),
          Expanded(
            child: RefreshIndicator(
              onRefresh: () async {
                ref.invalidate(myFinancialDocumentsProvider(_filter));
                await ref.read(myFinancialDocumentsProvider(_filter).future);
              },
              child: switch (documents) {
                AsyncError(:final error) => KhadraError(
                    message: ApiFailure.from(error).messageFor(l10n),
                    onRetry: () => ref.invalidate(myFinancialDocumentsProvider(_filter)),
                  ),
                AsyncData(:final value) when value.isEmpty => ListView(
                    children: [
                      KhadraEmpty(
                        icon: Icons.receipt_long_outlined,
                        title: _filter == InvoiceFilters.all ? l10n.invoicesEmpty : l10n.invoicesEmptyFilter,
                      ),
                    ],
                  ),
                AsyncData(:final value) when formats != null => ListView.separated(
                    controller: _scrollController,
                    padding: const EdgeInsets.fromLTRB(Space.lg, Space.xs, Space.lg, Space.bottomInset),
                    itemCount: value.items.length + 1,
                    separatorBuilder: (_, __) => const SizedBox(height: Space.sm),
                    itemBuilder: (_, index) => index == value.items.length
                        ? PagedListFooter(list: value)
                        : InvoiceRowCard(view: invoiceRow(value.items[index], l10n, formats)),
                  ),
                _ => const KhadraLoading(),
              },
            ),
          ),
        ],
      ),
    );
  }
}

/// One document in the list: the stored title and figure, the number, the booking
/// and when it was issued. The whole card opens the document.
class InvoiceRowCard extends StatelessWidget {
  const InvoiceRowCard({super.key, required this.view});

  final InvoiceRowView view;

  @override
  Widget build(BuildContext context) {
    const quiet = TextStyle(color: KhadraColors.neutral600, fontSize: 12);

    return KhadraCard(
      onTap: () => context.push(Routes.invoice(view.id)),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Wrap(
            spacing: Space.sm,
            runSpacing: Space.xs,
            crossAxisAlignment: WrapCrossAlignment.center,
            children: [
              Text(view.title, style: const TextStyle(fontSize: 14, fontWeight: FontWeight.w800, color: KhadraColors.text)),
              if (view.standing case final standing?) StandingBadge(standing: standing),
            ],
          ),
          const SizedBox(height: Space.xs),
          Wrap(
            spacing: Space.sm,
            runSpacing: 2,
            children: [
              LatinRun(view.number, style: quiet),
              if (view.version case final version?) Text(version, style: quiet),
              Text(view.booking, style: quiet),
            ],
          ),
          if (view.issued.isNotEmpty) Text(view.issued, style: quiet),
          const SizedBox(height: Space.sm),
          Row(
            crossAxisAlignment: CrossAxisAlignment.end,
            children: [
              Expanded(child: Text(view.headlineLabel, style: quiet)),
              Text(view.headline, style: const TextStyle(color: KhadraColors.price, fontSize: 15, fontWeight: FontWeight.w800)),
            ],
          ),
        ],
      ),
    );
  }
}
