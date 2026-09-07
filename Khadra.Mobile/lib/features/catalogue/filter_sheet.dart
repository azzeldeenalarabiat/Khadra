import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/dtos.dart';
import '../../core/providers.dart';
import '../../core/theme/khadra_theme.dart';
import '../../core/widgets/khadra_widgets.dart';
import '../../l10n/app_localizations.dart';
import '../auth/auth_form_widgets.dart';
import 'search_providers.dart';

/// The filters, all of them fed by the platform.
///
/// Not one chip's text is a literal. Cities and car types come from the lookup
/// endpoints in both languages; transmissions come from `/app-config`'s
/// vocabularies, where the NAME is the contract and the label is what is shown.
/// A category an administrator adds tomorrow appears here without a release.
Future<SearchFilter?> showFilterSheet({
  required BuildContext context,
  required SearchFilter current,
}) =>
    showModalBottomSheet<SearchFilter>(
      context: context,
      isScrollControlled: true,
      builder: (_) => _FilterSheet(current: current),
    );

class _FilterSheet extends ConsumerStatefulWidget {
  const _FilterSheet({required this.current});

  final SearchFilter current;

  @override
  ConsumerState<_FilterSheet> createState() => _FilterSheetState();
}

class _FilterSheetState extends ConsumerState<_FilterSheet> {
  late SearchFilter _draft = widget.current;
  late final TextEditingController _minPrice = TextEditingController(
    text: widget.current.minDailyRate?.toString() ?? '',
  );
  late final TextEditingController _maxPrice = TextEditingController(
    text: widget.current.maxDailyRate?.toString() ?? '',
  );

  @override
  void dispose() {
    _minPrice.dispose();
    _maxPrice.dispose();
    super.dispose();
  }

  SearchFilter get _withPrices => _draft.copyWith(
        minDailyRate: num.tryParse(_minPrice.text.trim()),
        maxDailyRate: num.tryParse(_maxPrice.text.trim()),
      );

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final arabic = ref.watch(isArabicProvider);
    final cities = ref.watch(citiesProvider).valueOrNull ?? const <Lookup>[];
    final carTypes = ref.watch(carTypesProvider).valueOrNull ?? const <Lookup>[];
    final transmissions =
        ref.watch(appConfigProvider).valueOrNull?.vocabularies.transmissions ??
            const <VocabularyEntry>[];

    return DraggableScrollableSheet(
      expand: false,
      initialChildSize: 0.85,
      maxChildSize: 0.95,
      builder: (context, controller) => Column(
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(Space.xl, Space.lg, Space.sm, 0),
            child: Row(
              children: [
                Expanded(
                  child: Text(
                    l10n.searchFilters,
                    style: const TextStyle(
                        fontSize: 18, fontWeight: FontWeight.w700),
                  ),
                ),
                TextButton(
                  onPressed: () => setState(() {
                    _draft = _draft.cleared();
                    _minPrice.clear();
                    _maxPrice.clear();
                  }),
                  child: Text(l10n.actionClearAll),
                ),
                IconButton(
                  onPressed: () => Navigator.of(context).pop(),
                  icon: const Icon(Icons.close),
                  tooltip: l10n.actionClose,
                ),
              ],
            ),
          ),
          Expanded(
            child: ListView(
              controller: controller,
              padding: const EdgeInsets.fromLTRB(Space.xl, Space.lg, Space.xl, Space.xl),
              children: [
                _ChipGroup(
                  title: l10n.searchCity,
                  anyLabel: l10n.searchAnyCity,
                  selected: _draft.cityId,
                  options: [
                    for (final city in cities)
                      (value: city.id, label: city.nameFor(arabic)),
                  ],
                  onChanged: (value) =>
                      setState(() => _draft = _draft.copyWith(cityId: value)),
                ),
                _ChipGroup(
                  title: l10n.searchCarType,
                  anyLabel: l10n.searchAnyCarType,
                  selected: _draft.carTypeId,
                  options: [
                    for (final type in carTypes)
                      (value: type.id, label: type.nameFor(arabic)),
                  ],
                  onChanged: (value) =>
                      setState(() => _draft = _draft.copyWith(carTypeId: value)),
                ),
                _ChipGroup(
                  title: l10n.searchTransmission,
                  anyLabel: l10n.searchAnyTransmission,
                  selected: _draft.transmission,
                  options: [
                    for (final entry in transmissions)
                      (value: entry.name, label: entry.labelFor(arabic)),
                  ],
                  onChanged: (value) => setState(
                      () => _draft = _draft.copyWith(transmission: value)),
                ),
                _ChipGroup(
                  title: l10n.searchSeats,
                  anyLabel: l10n.searchAnySeats,
                  selected: _draft.minSeats?.toString(),
                  options: [
                    for (final seats in const [2, 4, 5, 7])
                      (
                        value: seats.toString(),
                        label: l10n.searchMinimumSeats(seats)
                      ),
                  ],
                  onChanged: (value) => setState(() => _draft =
                      _draft.copyWith(minSeats: value == null ? null : int.parse(value))),
                ),

                const SizedBox(height: Space.sm),
                KhadraSectionTitle(l10n.searchPriceRange),
                Row(
                  children: [
                    Expanded(
                      child: KhadraField(
                        controller: _minPrice,
                        label: '${l10n.searchPriceRange} — ${l10n.labelOptional}',
                        keyboardType:
                            const TextInputType.numberWithOptions(decimal: true),
                        forceLtr: true,
                      ),
                    ),
                    const SizedBox(width: Space.md),
                    Expanded(
                      child: KhadraField(
                        controller: _maxPrice,
                        label: l10n.labelOptional,
                        keyboardType:
                            const TextInputType.numberWithOptions(decimal: true),
                        forceLtr: true,
                      ),
                    ),
                  ],
                ),

                SwitchListTile.adaptive(
                  value: _draft.deliveryOnly,
                  onChanged: (value) => setState(
                      () => _draft = _draft.copyWith(deliveryOnly: value)),
                  title: Text(l10n.searchDeliveryOnly),
                  contentPadding: EdgeInsets.zero,
                  activeThumbColor: KhadraColors.accent,
                ),
              ],
            ),
          ),
          SafeArea(
            child: Padding(
              padding: const EdgeInsets.fromLTRB(Space.xl, 0, Space.xl, Space.lg),
              child: KhadraSubmitButton(
                label: l10n.actionApply,
                onPressed: () => Navigator.of(context).pop(_withPrices),
              ),
            ),
          ),
        ],
      ),
    );
  }
}

/// A row of single-choice chips with an "any" option that clears it.
class _ChipGroup extends StatelessWidget {
  const _ChipGroup({
    required this.title,
    required this.anyLabel,
    required this.selected,
    required this.options,
    required this.onChanged,
  });

  final String title;
  final String anyLabel;
  final String? selected;
  final List<({String value, String label})> options;
  final ValueChanged<String?> onChanged;

  @override
  Widget build(BuildContext context) {
    // A lookup that has not loaded shows nothing rather than an empty heading with
    // one dead "any" chip under it.
    if (options.isEmpty) return const SizedBox.shrink();

    return Padding(
      padding: const EdgeInsets.only(bottom: Space.lg),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          KhadraSectionTitle(title),
          Wrap(
            spacing: Space.sm,
            runSpacing: Space.sm,
            children: [
              ChoiceChip(
                label: Text(anyLabel),
                selected: selected == null,
                onSelected: (_) => onChanged(null),
              ),
              for (final option in options)
                ChoiceChip(
                  label: Text(option.label),
                  selected: selected == option.value,
                  onSelected: (_) => onChanged(
                      selected == option.value ? null : option.value),
                ),
            ],
          ),
        ],
      ),
    );
  }
}
