import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_failure.dart';
import '../../core/api/api_failure_messages.dart';
import '../../core/providers.dart';
import '../../core/theme/khadra_theme.dart';
import '../../core/widgets/khadra_widgets.dart';
import '../../l10n/app_localizations.dart';
import 'date_range_sheet.dart';
import 'filter_sheet.dart';
import 'search_providers.dart';
import 'vehicle_card.dart';

/// The shop window.
///
/// Anonymous by design, settled with the owner: daily rates, delivery fees and the
/// deposit percentage are public prices, and a marketplace that demands a sign-up
/// before it will show a car converts badly. Booking still needs an account.
class SearchScreen extends ConsumerStatefulWidget {
  const SearchScreen({super.key});

  @override
  ConsumerState<SearchScreen> createState() => _SearchScreenState();
}

class _SearchScreenState extends ConsumerState<SearchScreen> {
  final _searchController = TextEditingController();
  final _scrollController = ScrollController();
  Timer? _debounce;

  @override
  void initState() {
    super.initState();
    _searchController.text = ref.read(searchFilterProvider).text ?? '';
    _scrollController.addListener(_onScroll);
  }

  @override
  void dispose() {
    _debounce?.cancel();
    _searchController.dispose();
    _scrollController.removeListener(_onScroll);
    _scrollController.dispose();
    super.dispose();
  }

  void _onScroll() {
    if (!_scrollController.hasClients) return;
    final position = _scrollController.position;
    if (position.pixels >= position.maxScrollExtent - 600) {
      unawaited(_loadMore());
    }
  }

  Future<void> _loadMore() async {
    try {
      await ref.read(searchResultsProvider.notifier).loadMore();
    } on ApiFailure catch (failure) {
      if (!mounted) return;
      showKhadraMessage(
        context,
        failure.messageFor(AppLocalizations.of(context)),
        isError: true,
      );
    }
  }

  /// Typing does not fire a request per keystroke. Every one of these is a full
  /// catalogue query, and a Jordanian mobile network is not the place to send ten
  /// of them for one word.
  void _onSearchChanged(String value) {
    _debounce?.cancel();
    _debounce = Timer(const Duration(milliseconds: 350), () {
      final text = value.trim();
      ref.read(searchFilterProvider.notifier).update(
            (filter) => filter.copyWith(text: text.isEmpty ? null : text),
          );
    });
  }

  Future<void> _openFilters() async {
    final current = ref.read(searchFilterProvider);
    final updated = await showFilterSheet(context: context, current: current);
    if (updated != null) {
      ref.read(searchFilterProvider.notifier).state = updated;
    }
  }

  Future<void> _openDates() async {
    final current = ref.read(searchFilterProvider);
    final hadDates = current.hasDates;

    final chosen = await showDateRangeSheet(
      context: context,
      ref: ref,
      initial: hadDates
          ? ChosenDates(current.pickupAt!, current.returnAt!)
          : null,
    );

    if (chosen != null) {
      ref.read(searchFilterProvider.notifier).update(
            (filter) => filter.copyWith(
              pickupAt: chosen.pickupAt,
              returnAt: chosen.returnAt,
            ),
          );
    } else if (hadDates && mounted) {
      // The sheet was dismissed while it had dates, which is how it says
      // "cleared". Both fields go together: the API refuses half a period.
      ref.read(searchFilterProvider.notifier).update(
            (filter) => filter.copyWith(pickupAt: null, returnAt: null),
          );
    }
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final filter = ref.watch(searchFilterProvider);
    final results = ref.watch(searchResultsProvider);
    final formats = ref.watch(formatsProvider);

    return Scaffold(
      appBar: AppBar(
        titleSpacing: Space.lg,
        title: Row(
          children: [
            const KhadraLogo(size: 30),
            const SizedBox(width: Space.sm),
            Text(l10n.appName),
          ],
        ),
      ),
      body: RefreshIndicator(
        onRefresh: () => ref.refresh(searchResultsProvider.future),
        child: CustomScrollView(
          controller: _scrollController,
          slivers: [
            SliverToBoxAdapter(
              child: Padding(
                padding: const EdgeInsets.fromLTRB(
                    Space.lg, Space.md, Space.lg, Space.sm),
                child: Column(
                  children: [
                    TextField(
                      controller: _searchController,
                      onChanged: _onSearchChanged,
                      textInputAction: TextInputAction.search,
                      decoration: InputDecoration(
                        hintText: l10n.searchHint,
                        prefixIcon: const Icon(Icons.search),
                        suffixIcon: _searchController.text.isEmpty
                            ? null
                            : IconButton(
                                icon: const Icon(Icons.clear),
                                onPressed: () {
                                  _searchController.clear();
                                  _onSearchChanged('');
                                  setState(() {});
                                },
                              ),
                        contentPadding: const EdgeInsets.symmetric(
                            horizontal: Space.lg, vertical: 0),
                      ),
                    ),
                    const SizedBox(height: Space.md),
                    Row(
                      children: [
                        Expanded(
                          child: _ToolbarButton(
                            icon: Icons.date_range_outlined,
                            label: filter.hasDates && formats != null
                                ? formats.dateRange(
                                    filter.pickupAt!, filter.returnAt!)
                                : l10n.searchAnyDates,
                            active: filter.hasDates,
                            onTap: _openDates,
                          ),
                        ),
                        const SizedBox(width: Space.sm),
                        Expanded(
                          child: _ToolbarButton(
                            icon: Icons.tune,
                            label: filter.activeCount > 0
                                ? l10n.searchFiltersApplied(filter.activeCount)
                                : l10n.searchFilters,
                            active: filter.activeCount > 0,
                            onTap: _openFilters,
                          ),
                        ),
                      ],
                    ),
                  ],
                ),
              ),
            ),

            ...switch (results) {
              AsyncLoading() => [
                  const SliverFillRemaining(
                    hasScrollBody: false,
                    child: KhadraLoading(),
                  ),
                ],
              AsyncError(:final error) => [
                  SliverFillRemaining(
                    hasScrollBody: false,
                    child: KhadraError(
                      message: ApiFailure.from(error).messageFor(l10n),
                      onRetry: () => ref.invalidate(searchResultsProvider),
                    ),
                  ),
                ],
              AsyncData(:final value) => _resultSlivers(l10n, filter, value),
              _ => const <Widget>[],
            },
          ],
        ),
      ),
    );
  }

  List<Widget> _resultSlivers(
    AppLocalizations l10n,
    SearchFilter filter,
    SearchResults results,
  ) {
    if (results.listings.isEmpty) {
      return [
        SliverFillRemaining(
          hasScrollBody: false,
          child: KhadraEmpty(
            icon: Icons.no_transfer_outlined,
            title: l10n.searchEmptyTitle,
            // Two different empty states, because they mean different things. A
            // filtered search with no hits is the customer's to fix; a catalogue
            // with nothing in it at all is the platform's, and saying "try
            // widening the dates" to somebody who set none would be nonsense.
            body: filter.activeCount == 0 && (filter.text ?? '').isEmpty
                ? l10n.searchEmptyNoListings
                : l10n.searchEmptyBody,
            action: filter.activeCount == 0
                ? null
                : OutlinedButton(
                    onPressed: () => ref
                        .read(searchFilterProvider.notifier)
                        .state = const SearchFilter(),
                    child: Text(l10n.actionClearAll),
                  ),
          ),
        ),
      ];
    }

    return [
      SliverToBoxAdapter(
        child: Padding(
          padding: const EdgeInsets.fromLTRB(Space.lg, Space.sm, Space.lg, Space.md),
          child: Text(
            l10n.searchResults(results.totalCount),
            style: const TextStyle(
                color: KhadraColors.neutral600, fontSize: 13),
          ),
        ),
      ),
      SliverPadding(
        padding: const EdgeInsets.fromLTRB(Space.lg, 0, Space.lg, Space.lg),
        sliver: SliverList.separated(
          itemCount: results.listings.length,
          separatorBuilder: (_, __) => const SizedBox(height: Space.lg),
          itemBuilder: (_, index) =>
              VehicleCard(listing: results.listings[index]),
        ),
      ),
      SliverToBoxAdapter(
        child: Padding(
          padding: const EdgeInsets.fromLTRB(
              Space.lg, 0, Space.lg, Space.bottomInset),
          child: Column(
            children: [
              if (results.loadingMore) const KhadraLoading(compact: true),
              // The rating shown on every card is the GALLERY's. Saying so once at
              // the foot of the list is the honest way to explain a number that
              // would otherwise look like a score for the car.
              Text(
                l10n.reviewsRatingIsOfficeNote,
                textAlign: TextAlign.center,
                style: const TextStyle(
                    color: KhadraColors.neutral500, fontSize: 12),
              ),
            ],
          ),
        ),
      ),
    ];
  }
}

class _ToolbarButton extends StatelessWidget {
  const _ToolbarButton({
    required this.icon,
    required this.label,
    required this.active,
    required this.onTap,
  });

  final IconData icon;
  final String label;
  final bool active;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) => Material(
        color: active ? KhadraColors.accent100 : KhadraColors.surface,
        borderRadius: Radii.field,
        child: InkWell(
          onTap: onTap,
          borderRadius: Radii.field,
          child: Container(
            height: 46,
            padding: const EdgeInsets.symmetric(horizontal: Space.md),
            decoration: BoxDecoration(
              borderRadius: Radii.field,
              border: Border.all(
                color: active ? KhadraColors.accent300 : KhadraColors.neutral300,
              ),
            ),
            child: Row(
              children: [
                Icon(
                  icon,
                  size: 18,
                  color: active ? KhadraColors.accent : KhadraColors.neutral600,
                ),
                const SizedBox(width: Space.sm),
                Expanded(
                  child: Text(
                    label,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: TextStyle(
                      fontSize: 13,
                      fontWeight: active ? FontWeight.w600 : FontWeight.w500,
                      color: active ? KhadraColors.accent : KhadraColors.text,
                    ),
                  ),
                ),
              ],
            ),
          ),
        ),
      );
}
