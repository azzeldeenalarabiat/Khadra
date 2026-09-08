import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/dtos.dart';
import '../../core/format/formats.dart';
import '../../core/providers.dart';
import '../../core/theme/khadra_theme.dart';
import '../../core/widgets/khadra_widgets.dart';
import '../../l10n/app_localizations.dart';
import '../auth/auth_form_widgets.dart';

/// The rental period a customer is shopping for.
class ChosenDates {
  const ChosenDates(this.pickupAt, this.returnAt);

  final DateTime pickupAt;
  final DateTime returnAt;
}

/// Picks a rental period IN AMMAN.
///
/// Three things here are the server's, not the app's:
///
/// - **The zone.** Rentals are billed in Amman calendar days, so nine in the
///   morning means nine in Amman. A local `DateTime` sent as UTC would be nine
///   wherever the phone is, and could land on a different calendar day — and a
///   different number of billed days — from the one shown.
/// - **The three bounds.** How soon a rental may start, how far ahead it may be
///   booked, and how long it may run all come from `/app-config`. A picker that
///   guessed would offer a slot the server refuses.
/// - **The day count.** Never shown here as a price. The billed count comes back
///   on the quote, and this screen deliberately does not pre-empt it.
Future<ChosenDates?> showDateRangeSheet({
  required BuildContext context,
  required WidgetRef ref,
  ChosenDates? initial,
}) async {
  final config = ref.read(appConfigProvider).valueOrNull;
  final formats = ref.read(formatsProvider);
  if (config == null || formats == null) return null;

  return showModalBottomSheet<ChosenDates>(
    context: context,
    isScrollControlled: true,
    builder: (_) => _DateRangeSheet(
      config: config,
      formats: formats,
      initial: initial,
    ),
  );
}

class _DateRangeSheet extends StatefulWidget {
  const _DateRangeSheet({
    required this.config,
    required this.formats,
    this.initial,
  });

  final AppConfig config;
  final Formats formats;
  final ChosenDates? initial;

  @override
  State<_DateRangeSheet> createState() => _DateRangeSheetState();
}

class _DateRangeSheetState extends State<_DateRangeSheet> {
  late DateTime? _pickupDay;
  late DateTime? _returnDay;
  late TimeOfDay _pickupTime;
  late TimeOfDay _returnTime;

  Formats get _formats => widget.formats;

  /// The earliest calendar day a rental may start on, in Amman.
  ///
  /// Derived from the minimum LEAD TIME, which is elapsed time and has no zone —
  /// then converted, because which calendar day "two hours from now" falls on very
  /// much does have one.
  DateTime get _firstDay => _formats.ammanDay(
        DateTime.now().toUtc().add(
              Duration(minutes: widget.config.minimumBookingLeadTimeMinutes),
            ),
      );

  DateTime get _lastDay => _formats.ammanDay(
        DateTime.now().toUtc().add(
              Duration(days: widget.config.maxAdvanceBookingDays),
            ),
      );

  @override
  void initState() {
    super.initState();

    final initial = widget.initial;
    if (initial != null) {
      _pickupDay = _formats.ammanDay(initial.pickupAt);
      _returnDay = _formats.ammanDay(initial.returnAt);
      final pickup = _formats.toAmman(initial.pickupAt);
      final ret = _formats.toAmman(initial.returnAt);
      _pickupTime = TimeOfDay(hour: pickup.hour, minute: pickup.minute);
      _returnTime = TimeOfDay(hour: ret.hour, minute: ret.minute);
    } else {
      _pickupDay = null;
      _returnDay = null;
      // Mid-morning: inside the opening hours of any gallery that keeps normal
      // ones, so the common case does not open on a time the server refuses.
      _pickupTime = const TimeOfDay(hour: 10, minute: 0);
      _returnTime = const TimeOfDay(hour: 10, minute: 0);
    }
  }

  Future<void> _pickRange() async {
    final range = await showDateRangePicker(
      context: context,
      firstDate: _firstDay,
      lastDate: _lastDay,
      currentDate: _firstDay,
      initialDateRange: _pickupDay != null && _returnDay != null
          ? DateTimeRange(start: _pickupDay!, end: _returnDay!)
          : null,
      helpText: AppLocalizations.of(context).searchChooseDates,
    );

    if (range != null) {
      setState(() {
        _pickupDay = range.start;
        _returnDay = range.end;
      });
    }
  }

  Future<void> _pickTime({required bool pickup}) async {
    final chosen = await showTimePicker(
      context: context,
      initialTime: pickup ? _pickupTime : _returnTime,
    );
    if (chosen == null) return;
    setState(() {
      if (pickup) {
        _pickupTime = chosen;
      } else {
        _returnTime = chosen;
      }
    });
  }

  /// The two instants, built as AMMAN wall clock and handed over as UTC.
  ChosenDates? get _chosen {
    final from = _pickupDay;
    final to = _returnDay;
    if (from == null || to == null) return null;

    return ChosenDates(
      _formats.ammanInstant(from, _pickupTime.hour, _pickupTime.minute),
      _formats.ammanInstant(to, _returnTime.hour, _returnTime.minute),
    );
  }

  /// Only the bounds the picker itself owns. Everything else — opening hours,
  /// availability, the price — is the server's answer, and the app must not
  /// pre-judge any of it with a rule of its own.
  String? _localProblem(AppLocalizations l10n) {
    final chosen = _chosen;
    if (chosen == null) return null;

    if (!chosen.returnAt.isAfter(chosen.pickupAt)) {
      return l10n.validationReturnAfterPickup;
    }

    // Judged on CALENDAR days, the same rule the server bills by, so the number a
    // customer is stopped at here is the number the quote would have shown.
    final days = _formats.calendarDaysBetween(chosen.pickupAt, chosen.returnAt);
    final billed = days < 1 ? 1 : days;
    if (billed > widget.config.maxRentalDays) {
      return l10n.bookDays(widget.config.maxRentalDays);
    }

    return null;
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final chosen = _chosen;
    final problem = _localProblem(l10n);

    return SafeArea(
      child: Padding(
        padding: const EdgeInsets.all(Space.xl),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Row(
              children: [
                Expanded(
                  child: Text(
                    l10n.searchChooseDates,
                    style: const TextStyle(
                        fontSize: 18, fontWeight: FontWeight.w700),
                  ),
                ),
                IconButton(
                  onPressed: () => Navigator.of(context).pop(),
                  icon: const Icon(Icons.close),
                  tooltip: l10n.actionClose,
                ),
              ],
            ),
            const SizedBox(height: Space.sm),
            Text(
              l10n.searchDatesHelp,
              style: const TextStyle(
                  color: KhadraColors.neutral600, fontSize: 14, height: 1.45),
            ),
            const SizedBox(height: Space.lg),

            OutlinedButton.icon(
              onPressed: _pickRange,
              icon: const Icon(Icons.date_range_outlined),
              label: Text(
                chosen == null
                    ? l10n.searchChooseDates
                    : _formats.dateRange(chosen.pickupAt, chosen.returnAt),
              ),
            ),

            if (chosen != null) ...[
              const SizedBox(height: Space.lg),
              Row(
                children: [
                  Expanded(
                    child: _TimeButton(
                      label: l10n.searchPickup,
                      value: _formats.time(chosen.pickupAt),
                      onTap: () => _pickTime(pickup: true),
                    ),
                  ),
                  const SizedBox(width: Space.md),
                  Expanded(
                    child: _TimeButton(
                      label: l10n.searchReturn,
                      value: _formats.time(chosen.returnAt),
                      onTap: () => _pickTime(pickup: false),
                    ),
                  ),
                ],
              ),
              const SizedBox(height: Space.md),
              Text(
                l10n.timeAmmanNote,
                style: const TextStyle(
                    color: KhadraColors.neutral500, fontSize: 12),
              ),
            ],

            if (problem != null) ...[
              const SizedBox(height: Space.lg),
              KhadraNotice(title: problem, tone: NoticeTone.bad),
            ],

            const SizedBox(height: Space.xl),
            KhadraSubmitButton(
              label: l10n.actionApply,
              onPressed: chosen == null || problem != null
                  ? null
                  : () => Navigator.of(context).pop(chosen),
            ),
            if (widget.initial != null) ...[
              const SizedBox(height: Space.sm),
              TextButton(
                // Popping with a sentinel would need a second type; the caller
                // reads a null result as "cleared" only when it opened the sheet
                // with dates already set, which is exactly this branch.
                onPressed: () => Navigator.of(context).pop(),
                child: Text(l10n.searchClearDates),
              ),
            ],
          ],
        ),
      ),
    );
  }
}

class _TimeButton extends StatelessWidget {
  const _TimeButton({
    required this.label,
    required this.value,
    required this.onTap,
  });

  final String label;
  final String value;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) => InkWell(
        onTap: onTap,
        borderRadius: Radii.field,
        child: InputDecorator(
          decoration: InputDecoration(
            labelText: label,
            suffixIcon: const Icon(Icons.schedule_outlined, size: 20),
            contentPadding: const EdgeInsets.symmetric(
                horizontal: Space.md, vertical: Space.md),
          ),
          child: Text(value, style: const TextStyle(fontSize: 15)),
        ),
      );
}
