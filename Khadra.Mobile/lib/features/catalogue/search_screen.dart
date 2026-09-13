import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/dtos.dart';
import '../../core/api/api_failure.dart';
import '../../core/api/api_failure_messages.dart';
import '../../core/paging.dart';
import '../../core/providers.dart';
import '../../core/theme/khadra_theme.dart';
import '../../core/widgets/khadra_widgets.dart';
import '../../l10n/app_localizations.dart';
import '../bookings/booking_providers.dart';
import '../shortlist/shortlist_providers.dart';
import 'date_range_sheet.dart';
import 'filter_sheet.dart';
import 'landing.dart';
import 'search_providers.dart';
import 'vehicle_row.dart';

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
        onRefresh: () async {
          // The landing card is on this screen and goes stale for the same
          // reasons the results do -- an approval that landed while the app was
          // closed is exactly what somebody pulls to find.
          ref.invalidate(nextBookingProvider);
          ref.invalidate(searchResultsProvider);
          await ref.read(searchResultsProvider.future);
        },
        child: CustomScrollView(
          controller: _scrollController,
          slivers: [
            SliverToBoxAdapter(
              child: Padding(
                padding: const EdgeInsets.fromLTRB(
                    Space.lg, Space.md, Space.lg, Space.sm),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    // The landing state. Above the search box rather than below
                    // it, because the thing it carries — a deposit falling due —
                    // is more urgent than anything the customer came here to
                    // look for.
                    const SearchLanding(),
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
                    const SizedBox(height: Space.md),
                    _Toolbar(
                      buttons: [
                        _ToolbarButton(
                          icon: Icons.date_range_outlined,
                          label: filter.hasDates && formats != null
                              ? formats.dateRange(
                                  filter.pickupAt!, filter.returnAt!)
                              : l10n.searchAnyDates,
                          active: filter.hasDates,
                          onTap: _openDates,
                        ),
                        _ToolbarButton(
                          icon: Icons.tune,
                          label: filter.activeCount > 0
                              ? l10n.searchFiltersApplied(filter.activeCount)
                              : l10n.searchFilters,
                          active: filter.activeCount > 0,
                          onTap: _openFilters,
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
      SliverToBoxAdapter(
        child: Padding(
          padding: const EdgeInsets.fromLTRB(Space.lg, Space.sm, Space.lg, Space.md),
          child: Text(
            // The SERVER's count over the whole catalogue, not the length of the
            // pages loaded so far.
            l10n.searchResults(results.total),
            style: const TextStyle(
                color: KhadraColors.neutral600, fontSize: 13),
          ),
        ),
      ),
      SliverPadding(
        padding: const EdgeInsets.fromLTRB(Space.lg, 0, Space.lg, Space.lg),
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

/// The controls above the results, laid out for the words they actually hold.
///
/// They were two `Expanded` halves, which gives each exactly half the width no
/// matter what is written in it. In English that is fine — "Any dates" and
/// "1 filter" are short. In Arabic "عامل تصفية واحد" does not fit in half of a
/// 375-wide phone at all, so the label a customer needed most, the one saying a
/// filter was hiding results from them, was the one that arrived as "عامل تص…".
/// Turning the text size up did the same thing to English.
///
/// So the width follows the CONTENT. Each button asks for what it needs; if the
/// two together fit on one line they share the leftover in proportion, and if
/// they do not they stack full width rather than cropping. Nothing here is
/// measured against a particular language.
class _Toolbar extends StatelessWidget {
  const _Toolbar({required this.buttons});

  final List<_ToolbarButton> buttons;

  @override
  Widget build(BuildContext context) => LayoutBuilder(
        builder: (context, constraints) {
          final gaps = Space.sm * (buttons.length - 1);
          final wanted = [
            for (final button in buttons) button.widthIn(context),
          ];
          final total = wanted.fold<double>(0, (sum, w) => sum + w) + gaps;

          if (total > constraints.maxWidth) {
            return Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                for (var i = 0; i < buttons.length; i++) ...[
                  if (i > 0) const SizedBox(height: Space.sm),
                  buttons[i],
                ],
              ],
            );
          }

          return Row(
            children: [
              for (var i = 0; i < buttons.length; i++) ...[
                if (i > 0) const SizedBox(width: Space.sm),
                // Proportional, not equal. `Expanded` divides what is left after
                // the gaps, so a button asking for more of the line gets more of
                // it — and because the total fits, every one of them ends up with
                // at least what it asked for.
                Expanded(
                  flex: (wanted[i] * 100).round().clamp(1, 1 << 30),
                  child: buttons[i],
                ),
              ],
            ],
          );
        },
      );
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

  static const double _iconSize = 18;
  static const double _border = 1;
  static const double _minHeight = 46;

  TextStyle _styleIn(BuildContext context) {
    final style = DefaultTextStyle.of(context).style.merge(
          TextStyle(
            fontSize: 13,
            fontWeight: active ? FontWeight.w600 : FontWeight.w500,
            color: active ? KhadraColors.accent : KhadraColors.text,
          ),
        );

    // `Text` merges this itself when the reader has turned bold text on at the
    // system level. The measurement below has to do the same, or every label is
    // measured light and painted bold — putting an ellipsis on exactly the labels
    // of the customers who asked for the larger, heavier text.
    return MediaQuery.boldTextOf(context)
        ? style.merge(const TextStyle(fontWeight: FontWeight.bold))
        : style;
  }

  /// How wide this button has to be for its label to be whole.
  ///
  /// Resolved against the ambient default text style and the reader's own text
  /// size, so the answer is in the app's real faces rather than the platform's.
  double widthIn(BuildContext context) {
    final painter = TextPainter(
      text: TextSpan(text: label, style: _styleIn(context)),
      textDirection: Directionality.of(context),
      textScaler: MediaQuery.textScalerOf(context),
      maxLines: 1,
    )..layout();

    final text = painter.width;
    painter.dispose();

    return text +
        _iconSize +
        Space.sm +
        Space.md * 2 +
        _border * 2 +
        // A hair of slack, so a width that rounds down by a fraction of a pixel
        // does not put an ellipsis on a label that fits.
        1;
  }

  @override
  Widget build(BuildContext context) => Material(
        color: active ? KhadraColors.accent100 : KhadraColors.surface,
        borderRadius: Radii.field,
        child: InkWell(
          onTap: onTap,
          borderRadius: Radii.field,
          child: Container(
            // A MINIMUM, not a height. 46 was measured at the default text size
            // in Latin; Noto Kufi Arabic's line box is deeper, and text scaling
            // goes to 1.4 here, so a fixed box crops the label from the top and
            // the bottom instead of growing.
            constraints: const BoxConstraints(minHeight: _minHeight),
            padding: const EdgeInsets.symmetric(
                horizontal: Space.md, vertical: Space.sm),
            decoration: BoxDecoration(
              borderRadius: Radii.field,
              border: Border.all(
                width: _border,
                color: active ? KhadraColors.accent300 : KhadraColors.neutral300,
              ),
            ),
            child: Row(
              children: [
                Icon(
                  icon,
                  size: _iconSize,
                  color: active ? KhadraColors.accent : KhadraColors.neutral600,
                ),
                const SizedBox(width: Space.sm),
                Expanded(
                  child: Text(
                    label,
                    maxLines: 1,
                    // The last resort, not the plan. With the width following the
                    // label this should never fire; it is here so a translation
                    // nobody anticipated degrades instead of overflowing.
                    overflow: TextOverflow.ellipsis,
                    style: _styleIn(context),
                  ),
                ),
              ],
            ),
          ),
        ),
      );
}
