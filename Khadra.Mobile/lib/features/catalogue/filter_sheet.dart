import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/dtos.dart';
import '../../core/providers.dart';
import '../../core/theme/khadra_theme.dart';
import '../../core/widgets/khadra_widgets.dart';
import '../../l10n/app_localizations.dart';
import '../auth/auth_form_widgets.dart';
import 'search_providers.dart';

/// The filters beyond where, when and what kind — all of them fed by the platform.
///
/// Not one chip's text is a literal. Transmissions come from `/app-config`'s
/// vocabularies, where the NAME is the contract and the label is what is shown;
/// the seat choices are the counts the bookable catalogue actually has. The city,
/// the dates and the category are on Home itself, where a customer sees them
/// without opening anything.
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

  /// A range that can never match anything: the top below the bottom.
  bool get _priceRangeInverted {
    final min = num.tryParse(_minPrice.text.trim());
    final max = num.tryParse(_maxPrice.text.trim());
    return min != null && max != null && max < min;
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final arabic = ref.watch(isArabicProvider);
    final transmissions =
        ref.watch(appConfigProvider).valueOrNull?.vocabularies.transmissions ??
            const <VocabularyEntry>[];
    final seats =
        ref.watch(catalogueFacetsProvider).valueOrNull?.seats ?? const <int>[];

    return DraggableScrollableSheet(
      expand: false,
      initialChildSize: 0.85,
      maxChildSize: 0.95,
      builder: (context, controller) => Column(
        children: [
          Padding(
            // No top padding: the sheet's drag handle already stands the header off
            // the edge, and adding to it opened a band of empty white above the
            // one word that says what the sheet is.
            //
            // DIRECTIONAL, because the two ends are not the same: the title needs
            // the full inset and the close button sits tight against the far edge.
            // Written as LTRB it put the wide inset on the left in Arabic too, so
            // the heading floated off its own margin while the X pressed into it.
            padding: const EdgeInsetsDirectional.fromSTEB(
                Space.lg, 0, Space.sm, 0),
            child: Row(
              children: [
                Expanded(
                  child: Text(
                    l10n.searchFilters,
                    style: const TextStyle(
                        fontSize: 18, fontWeight: FontWeight.w800),
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
              padding: const EdgeInsets.fromLTRB(
                  Space.lg, Space.md, Space.lg, Space.lg),
              children: [
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
                  // The seat counts the bookable catalogue has, from the server. The
                  // list typed in here offered "at least 2 seats" to a platform with
                  // no two-seaters, and could never offer the nine-seat van an office
                  // lists tomorrow. Nothing to offer hides the group.
                  options: [
                    for (final count in seats)
                      (
                        value: count.toString(),
                        label: l10n.searchMinimumSeats(count)
                      ),
                  ],
                  onChanged: (value) => setState(() => _draft =
                      _draft.copyWith(minSeats: value == null ? null : int.parse(value))),
                ),

                const SizedBox(height: Space.sm),
                KhadraFieldLabel(l10n.searchPriceRange),
                Row(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Expanded(
                      // Each end says which end it is. They read "Price range —
                      // optional" and "Optional", which told a customer nothing
                      // about which box was the cheap end.
                      child: KhadraField(
                        controller: _minPrice,
                        label: l10n.searchPriceFrom,
                        keyboardType:
                            const TextInputType.numberWithOptions(decimal: true),
                        forceLtr: true,
                        onChanged: (_) => setState(() {}),
                      ),
                    ),
                    const SizedBox(width: Space.md),
                    Expanded(
                      child: KhadraField(
                        controller: _maxPrice,
                        label: l10n.searchPriceTo,
                        keyboardType:
                            const TextInputType.numberWithOptions(decimal: true),
                        forceLtr: true,
                        onChanged: (_) => setState(() {}),
                      ),
                    ),
                  ],
                ),
                // An inverted range returns nothing at all, and a customer
                // staring at "0 cars" has no way to see that the two boxes are
                // the reason. Said before the search rather than after it.
                if (_priceRangeInverted)
                  Padding(
                    padding: const EdgeInsets.only(bottom: Space.lg),
                    child: Text(
                      l10n.searchPriceRangeInverted,
                      style: const TextStyle(
                          color: KhadraColors.bad, fontSize: 13, height: 1.4),
                    ),
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
          // The button is a FOOTER, not the next thing in the list: the hairline
          // is what stops it reading as an answer to whichever control happens to
          // have scrolled to the bottom of the sheet.
          const Divider(height: 1),
          SafeArea(
            child: Padding(
              padding: const EdgeInsets.fromLTRB(
                  Space.lg, Space.md, Space.lg, Space.md),
              child: KhadraSubmitButton(
                label: l10n.actionApply,
                onPressed: _priceRangeInverted
                    ? null
                    : () => Navigator.of(context).pop(_withPrices),
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
          KhadraFieldLabel(title),
          Wrap(
            spacing: Space.sm,
            runSpacing: Space.sm,
            children: [
              KhadraChoiceChip(
                label: anyLabel,
                selected: selected == null,
                onTap: () => onChanged(null),
              ),
              for (final option in options)
                KhadraChoiceChip(
                  label: option.label,
                  selected: selected == option.value,
                  onTap: () => onChanged(
                      selected == option.value ? null : option.value),
                ),
            ],
          ),
        ],
      ),
    );
  }
}
