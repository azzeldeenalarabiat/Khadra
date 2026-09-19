import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/dtos.dart';
import '../../core/api/api_failure.dart';
import '../../core/api/api_failure_messages.dart';
import '../../core/paging.dart';
import '../../core/theme/khadra_theme.dart';
import '../../core/widgets/khadra_widgets.dart';
import '../../l10n/app_localizations.dart';
import '../bookings/booking_providers.dart';
import '../shortlist/shortlist_providers.dart';
import 'date_range_sheet.dart';
import 'filter_sheet.dart';
import 'landing.dart';
import 'search_header.dart';
import 'search_providers.dart';
import 'vehicle_row.dart';

/// The shop window.
///
/// Anonymous by design, settled with the owner: daily rates, delivery fees and the
/// deposit percentage are public prices, and a marketplace that demands a sign-up
/// before it will show a car converts badly. Booking still needs an account.
///
/// Laid out the way a rental is thought about: where and when first, then the make
/// or model, then the kind of car, then how many there are — with every other
/// filter one button away rather than in the way.
class SearchScreen extends ConsumerStatefulWidget {
  const SearchScreen({super.key});

  @override
  ConsumerState<SearchScreen> createState() => _SearchScreenState();
}

class _SearchScreenState extends ConsumerState<SearchScreen> {
  final _searchController = TextEditingController();
  final _scrollController = ScrollController();
  late final EndOfListLoader _loader = EndOfListLoader(
    controller: _scrollController,
    onReachEnd: () => unawaited(_loadMore()),
  );
  Timer? _debounce;

  @override
  void initState() {
    super.initState();
    _searchController.text = ref.read(searchFilterProvider).text ?? '';
    _loader; // Attaches the listener.
  }

  @override
  void dispose() {
    _debounce?.cancel();
    _searchController.dispose();
    _loader.dispose();
    _scrollController.dispose();
    super.dispose();
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

  /// Typing does not fire a REQUEST per keystroke. Every one of these is a full
  /// catalogue query, and a Jordanian mobile network is not the place to send ten
  /// of them for one word.
  ///
  /// The repaint is not debounced, only the query: the clear button is drawn from
  /// whether the box is empty, and waiting 350 ms to draw it made the control
  /// appear a beat after the character that should have summoned it.
  void _onSearchChanged(String value) {
    setState(() {});
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
        onRefresh: () async {
          // The landing card is on this screen and goes stale for the same
          // reasons the results do -- an approval that landed while the app was
          // closed is exactly what somebody pulls to find. So do the categories:
          // a car listed since the app opened can bring one with it.
          ref.invalidate(nextBookingProvider);
          ref.invalidate(catalogueFacetsProvider);
          ref.invalidate(searchResultsProvider);
          await ref.read(searchResultsProvider.future);
        },
        child: CustomScrollView(
          controller: _scrollController,
          slivers: [
            SliverToBoxAdapter(
              child: Padding(
                padding: const EdgeInsets.only(top: Space.md, bottom: Space.sm),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    Padding(
                      padding: const EdgeInsets.symmetric(horizontal: Space.lg),
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.stretch,
                        children: [
                          // Above everything, because what it carries — a deposit
                          // falling due — is more urgent than anything the customer
                          // came here to look for.
                          const SearchLanding(),
                          SearchWhereWhen(onChooseDates: _openDates),
                          const SizedBox(height: Space.md),
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
                                      tooltip: l10n.actionClearAll,
                                      onPressed: () {
                                        _searchController.clear();
                                        _onSearchChanged('');
                                      },
                                    ),
                              contentPadding: const EdgeInsets.symmetric(
                                  horizontal: Space.lg, vertical: 0),
                            ),
                          ),
                        ],
                      ),
                    ),
                    const SizedBox(height: Space.md),
                    // Edge to edge: the row keeps its own gutter so it scrolls under
                    // the page's margins.
                    const SearchCarTypeChips(),
                    const SizedBox(height: Space.md),
                    Padding(
                      padding: const EdgeInsets.symmetric(horizontal: Space.lg),
                      child: SearchResultsBar(onOpenFilters: _openFilters),
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
    PagedList<CatalogueListing> results,
  ) {
    // ONE question for the whole page, asked after the frame rather than during
    // it: a provider must not be written to while the tree that reads it is being
    // built. The notifier skips ids it has already asked about, so scrolling back
    // up costs nothing and a second page does not re-ask for the first.
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted) return;
      unawaited(ref.read(savedVehiclesProvider.notifier).learn(
            [for (final listing in results.items) listing.vehicleId],
          ));
    });

    if (results.isEmpty) {
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
      SliverPadding(
        padding: const EdgeInsets.fromLTRB(Space.lg, Space.sm, Space.lg, Space.lg),
        sliver: SliverList.separated(
          itemCount: results.items.length,
          separatorBuilder: (_, __) => const SizedBox(height: Space.lg),
          itemBuilder: (_, index) => VehicleRow(
            listing: results.items[index],
            trailing: VehicleRowSaveButton(
              vehicleId: results.items[index].vehicleId,
            ),
          ),
        ),
      ),
      SliverToBoxAdapter(
        child: Padding(
          padding: const EdgeInsets.fromLTRB(
              Space.lg, 0, Space.lg, Space.bottomInset),
          child: Column(
            children: [
              PagedListFooter(list: results),
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
