import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/dtos.dart';
import '../../core/providers.dart';
import '../../core/theme/khadra_theme.dart';
import '../../core/widgets/khadra_widgets.dart';
import '../../l10n/app_localizations.dart';
import 'search_providers.dart';

/// Where and when: the two questions a rental starts from, first on Home.
///
/// Both answers are stated in place rather than hidden behind a button, so a
/// customer can see at a glance what the results below are for. Nothing here is a
/// default the app invented: the city is the platform's lookup or "any", and the
/// period is the one the customer chose or "any dates".
class SearchWhereWhen extends ConsumerWidget {
  const SearchWhereWhen({super.key, required this.onChooseDates});

  /// The date sheet belongs to the screen, which owns what dismissing it means.
  final VoidCallback onChooseDates;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);
    final filter = ref.watch(searchFilterProvider);
    final arabic = ref.watch(isArabicProvider);
    final formats = ref.watch(formatsProvider);
    final cities = ref.watch(citiesProvider).valueOrNull ?? const <Lookup>[];

    // By id, in the reader's language. A chosen city the lookup cannot name — it
    // has not loaded, or was retired since — states no name rather than "any".
    final cityId = filter.cityId;
    final city = cityId == null
        ? l10n.searchAnyCity
        : cities.where((candidate) => candidate.id == cityId).firstOrNull?.nameFor(arabic);

    final period = filter.hasDates && formats != null
        ? l10n.searchPeriodValue(
            formats.dateTime(filter.pickupAt!),
            formats.dateTime(filter.returnAt!),
          )
        : l10n.searchAnyDates;

    return Material(
      color: KhadraColors.surface,
      shape: const RoundedRectangleBorder(
        borderRadius: Radii.feature,
        side: BorderSide(color: KhadraColors.neutral200),
      ),
      clipBehavior: Clip.antiAlias,
      child: Column(
        children: [
          _WhereWhenRow(
            icon: Icons.place_outlined,
            label: l10n.searchPickupLocation,
            value: city,
            chosen: cityId != null,
            onTap: () => _chooseCity(context, ref, cities, arabic),
          ),
          const Divider(height: 1, indent: Space.lg, endIndent: Space.lg),
          _WhereWhenRow(
            icon: Icons.date_range_outlined,
            label: l10n.searchRentalPeriod,
            value: period,
            chosen: filter.hasDates,
            onTap: onChooseDates,
          ),
        ],
      ),
    );
  }

  Future<void> _chooseCity(
    BuildContext context,
    WidgetRef ref,
    List<Lookup> cities,
    bool arabic,
  ) async {
    final current = ref.read(searchFilterProvider).cityId;
    final chosen = await showModalBottomSheet<({String? cityId})>(
      context: context,
      isScrollControlled: true,
      builder: (_) => _CitySheet(cities: cities, selected: current, arabic: arabic),
    );

    // Dismissed: nothing was chosen, so nothing changes. "Any city" is a choice,
    // and arrives as a record holding null rather than as null itself.
    if (chosen == null || !context.mounted) return;
    ref
        .read(searchFilterProvider.notifier)
        .update((filter) => filter.copyWith(cityId: chosen.cityId));
  }
}

class _WhereWhenRow extends StatelessWidget {
  const _WhereWhenRow({
    required this.icon,
    required this.label,
    required this.value,
    required this.chosen,
    required this.onTap,
  });

  final IconData icon;
  final String label;
  final String? value;

  /// Whether the value is the customer's answer rather than "any".
  final bool chosen;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final text = Theme.of(context).textTheme;

    return MergeSemantics(
      child: InkWell(
        onTap: onTap,
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: Space.lg, vertical: Space.md),
          child: Row(
            children: [
              DecoratedBox(
                decoration: const BoxDecoration(
                  color: KhadraColors.accent100,
                  borderRadius: Radii.field,
                ),
                child: Padding(
                  padding: const EdgeInsets.all(Space.sm),
                  child: Icon(icon, size: 20, color: KhadraColors.accent),
                ),
              ),
              const SizedBox(width: Space.md),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(label, style: text.labelMedium),
                    if (value != null)
                      Text(
                        value!,
                        maxLines: 2,
                        overflow: TextOverflow.ellipsis,
                        style: text.titleSmall?.copyWith(
                          color: chosen ? KhadraColors.text : KhadraColors.neutral600,
                        ),
                      ),
                  ],
                ),
              ),
              const SizedBox(width: Space.sm),
              const KhadraDisclosure(),
            ],
          ),
        ),
      ),
    );
  }
}

/// "Any city", then the platform's cities in its own order.
class _CitySheet extends StatelessWidget {
  const _CitySheet({
    required this.cities,
    required this.selected,
    required this.arabic,
  });

  final List<Lookup> cities;
  final String? selected;
  final bool arabic;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final options = <({String? id, String label})>[
      (id: null, label: l10n.searchAnyCity),
      for (final city in cities) (id: city.id, label: city.nameFor(arabic)),
    ];

    return SafeArea(
      child: ConstrainedBox(
        // The number of cities is the platform's to grow; the list scrolls rather
        // than pushing the sheet off the top of the screen.
        constraints: BoxConstraints(maxHeight: MediaQuery.sizeOf(context).height * 0.7),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Padding(
              padding: const EdgeInsets.fromLTRB(Space.lg, 0, Space.lg, Space.sm),
              child: Text(
                l10n.searchPickupLocation,
                style: Theme.of(context).textTheme.titleLarge,
              ),
            ),
            Flexible(
              child: ListView(
                shrinkWrap: true,
                children: [
                  for (final option in options)
                    ListTile(
                      title: Text(option.label),
                      selected: option.id == selected,
                      selectedColor: KhadraColors.accent,
                      trailing: option.id == selected
                          ? const Icon(Icons.check, color: KhadraColors.accent)
                          : null,
                      onTap: () => Navigator.of(context).pop((cityId: option.id)),
                    ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}

/// "All", then the car types the catalogue actually has cars in, in the
/// platform's own order.
///
/// A row that scrolls sideways and sizes itself to its chips: the number of
/// categories is the platform's to grow, and a list given a fixed height is what
/// cropped Arabic chips top and bottom before.
class SearchCarTypeChips extends ConsumerWidget {
  const SearchCarTypeChips({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);
    final arabic = ref.watch(isArabicProvider);
    final selected = ref.watch(searchFilterProvider.select((filter) => filter.carTypeId));
    final types = ref.watch(carTypesProvider).valueOrNull ?? const <Lookup>[];

    // Null when the catalogue could not say what it holds. Every active type is
    // offered then: a chip with no car behind it leads to an honest empty page,
    // which is better than no categories at all.
    final facets = ref.watch(catalogueFacetsProvider).valueOrNull;

    final offered = [
      for (final type in types)
        // The chosen one stays even when its last car has gone. Otherwise a customer
        // is filtered by a chip they can no longer see, and so cannot take back.
        if (facets == null || facets.carTypeIds.contains(type.id) || type.id == selected)
          type,
    ];
    if (offered.isEmpty) return const SizedBox.shrink();

    void choose(String? id) => ref
        .read(searchFilterProvider.notifier)
        .update((filter) => filter.copyWith(carTypeId: id));

    return SingleChildScrollView(
      scrollDirection: Axis.horizontal,
      // The gutter is INSIDE the scroll view, so the row scrolls under the page's
      // edges rather than being cut off at them.
      padding: const EdgeInsets.symmetric(horizontal: Space.lg),
      child: Row(
        children: [
          KhadraChoiceChip(
            label: l10n.searchAllCarTypes,
            selected: selected == null,
            onTap: () => choose(null),
          ),
          for (final type in offered) ...[
            const SizedBox(width: Space.sm),
            KhadraChoiceChip(
              label: type.nameFor(arabic),
              selected: selected == type.id,
              onTap: () => choose(selected == type.id ? null : type.id),
            ),
          ],
        ],
      ),
    );
  }
}

/// How many cars the search found, and the way to the rest of the filters.
class SearchResultsBar extends ConsumerWidget {
  const SearchResultsBar({super.key, required this.onOpenFilters});

  final VoidCallback onOpenFilters;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);
    final filter = ref.watch(searchFilterProvider);
    final results = ref.watch(searchResultsProvider);

    // The SERVER's count over the whole search, never the pages loaded so far —
    // and only once it answers the search on screen, not the last one's while
    // this one loads.
    final total = results.isLoading || !results.hasValue ? null : results.requireValue.total;
    final count = switch (total) {
      null => '',
      // "Available" only with dates. Without a period there is no availability to
      // speak of, and every listed car is simply a car.
      int total when filter.hasDates => l10n.searchResultsAvailable(total),
      int total => l10n.searchResults(total),
    };

    // A Wrap, so the button drops under the count rather than squeezing it when the
    // two do not fit on one line — in Arabic, or at a large text size.
    return Wrap(
      alignment: WrapAlignment.spaceBetween,
      crossAxisAlignment: WrapCrossAlignment.center,
      spacing: Space.sm,
      runSpacing: Space.xs,
      children: [
        Text(count, style: Theme.of(context).textTheme.titleSmall),
        _FiltersButton(count: filter.sheetCount, onTap: onOpenFilters),
      ],
    );
  }
}

class _FiltersButton extends StatelessWidget {
  const _FiltersButton({required this.count, required this.onTap});

  final int count;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final active = count > 0;

    return OutlinedButton.icon(
      onPressed: onTap,
      style: OutlinedButton.styleFrom(
        // Sized to its label, not to the row: the theme's outlined button is a
        // full-width form button.
        minimumSize: const Size(0, 40),
        padding: const EdgeInsets.symmetric(horizontal: Space.md),
        foregroundColor: active ? KhadraColors.accent : KhadraColors.text,
        backgroundColor: active ? KhadraColors.accent100 : KhadraColors.surface,
        side: BorderSide(color: active ? KhadraColors.accent : KhadraColors.neutral300),
      ),
      icon: const Icon(Icons.tune, size: 18),
      label: Text(active ? l10n.searchFiltersApplied(count) : l10n.searchFilters),
    );
  }
}
