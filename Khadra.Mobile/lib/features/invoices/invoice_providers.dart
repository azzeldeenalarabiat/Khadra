import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/dtos.dart';
import '../../core/paging.dart';
import '../../core/providers.dart';
import '../../core/uploads/document_viewer.dart';

/// The Invoices & Receipts filters: every document, or one kind of it. The kinds
/// are the server's own names; a kind this build does not know still appears
/// under [all].
abstract final class InvoiceFilters {
  static const all = 'all';

  static const ordered = <String>[all, ...FinancialDocumentTypes.known];
}

/// Invoices & Receipts (payments Phase 5b; owner, 2026-09-27): every issued
/// document of the customer's, newest issued first as the server orders them —
/// every version, earlier and voided ones included, each marked, because an
/// issued record never disappears from its owner's account.
class MyFinancialDocumentsNotifier
    extends AutoDisposeFamilyAsyncNotifier<PagedList<FinancialDocumentRow>, String> {
  static const _pageSize = 20;

  @override
  Future<PagedList<FinancialDocumentRow>> build(String filter) async {
    // The one fact this cares about, as `MyBookingsNotifier` watches it: a token
    // rotation must not re-read the list.
    final signedIn = ref.watch(sessionProvider.select((state) => state.isSignedIn));
    if (!signedIn) return const PagedList<FinancialDocumentRow>.empty();
    return _fetch(filter, page: 1, existing: const []);
  }

  Future<void> loadMore() async {
    await loadNextPage<FinancialDocumentRow>(
      current: state.valueOrNull,
      emit: (next) => state = AsyncData(next),
      fetch: (page, existing) => _fetch(arg, page: page, existing: existing),
    );
  }

  Future<PagedList<FinancialDocumentRow>> _fetch(
    String filter, {
    required int page,
    required List<FinancialDocumentRow> existing,
  }) async {
    final result = await ref.read(apiProvider).myFinancialDocuments(
          type: filter == InvoiceFilters.all ? null : filter,
          page: page,
          pageSize: _pageSize,
        );

    return PagedList<FinancialDocumentRow>(
      items: [...existing, ...result.items],
      page: result.page,
      total: result.totalCount,
      hasMore: result.hasNext,
    );
  }
}

final myFinancialDocumentsProvider = AsyncNotifierProvider.autoDispose
    .family<MyFinancialDocumentsNotifier, PagedList<FinancialDocumentRow>, String>(
  MyFinancialDocumentsNotifier.new,
);

/// One document exactly as issued, or null when it is not there for this
/// customer.
final financialDocumentProvider =
    FutureProvider.autoDispose.family<FinancialDocumentPage?, String>((ref, documentId) async {
  return ref.watch(apiProvider).financialDocument(documentId);
});

/// Opens a fetched file in the platform's viewer (payments Phase 6): the
/// document's PDF, written to this app's private cache first. A seam, so a
/// widget test sees what the screen asked to open without a platform channel.
typedef FileOpener = Future<bool> Function({
  required Uint8List bytes,
  required String? contentType,
  required String documentId,
});

final fileOpenerProvider = Provider<FileOpener>((ref) => DocumentViewer.open);
