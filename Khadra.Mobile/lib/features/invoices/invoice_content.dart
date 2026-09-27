import 'package:flutter/material.dart';

import '../../core/format/formats.dart';
import '../../core/theme/khadra_theme.dart';
import '../../core/widgets/khadra_widgets.dart';
import 'invoice_presentation.dart';

/// A document's standing as a badge — none is drawn for a current one — with the
/// server's own name, isolated, for a standing this build does not know.
class StandingBadge extends StatelessWidget {
  const StandingBadge({super.key, required this.standing});

  final StandingView standing;

  @override
  Widget build(BuildContext context) => KhadraBadge(
        label: standing.unknown ? Formats.isolate(standing.label) : standing.label,
        colour: switch (standing.tone) {
          StandingTone.bad => KhadraColors.bad,
          StandingTone.ok => KhadraColors.ok,
          StandingTone.neutral => KhadraColors.neutral600,
        },
      );
}

/// An issued financial document, exactly as it was issued (payments Phase 5b):
/// its title, its headline figure, its sections in their stored order, its time
/// note and its notice. Every word and figure is the stored document's own,
/// already chosen in the app's language by `documentBody`; this adds none.
///
/// Each label sits ABOVE its value. A document's values are long — an issuer's
/// legal name, an address, a number like TEST-STM-2026-000011 — and beside a label
/// on a 360-pixel phone they would wrap into fragments or overflow; the website
/// stacks them the same way on phones.
class InvoiceContent extends StatelessWidget {
  const InvoiceContent({super.key, required this.body});

  final DocumentBodyView body;

  @override
  Widget build(BuildContext context) => KhadraCard(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text(
              body.title,
              style: const TextStyle(fontSize: 17, fontWeight: FontWeight.w800, color: KhadraColors.text),
            ),
            const SizedBox(height: Space.xs),
            Wrap(
              spacing: Space.sm,
              runSpacing: 2,
              crossAxisAlignment: WrapCrossAlignment.end,
              children: [
                Text(body.headlineLabel, style: const TextStyle(color: KhadraColors.neutral600, fontSize: 13)),
                Text(
                  body.headline,
                  style: const TextStyle(color: KhadraColors.price, fontSize: 18, fontWeight: FontWeight.w800),
                ),
              ],
            ),
            const Padding(
              padding: EdgeInsets.symmetric(vertical: Space.md),
              child: Divider(height: 1, color: KhadraColors.divider),
            ),
            for (final section in body.sections) _Section(section: section),
            if (body.timeNote case final note?)
              Padding(
                padding: const EdgeInsets.only(top: Space.md),
                child: Text(note, style: const TextStyle(color: KhadraColors.neutral600, fontSize: 12, height: 1.45)),
              ),
            if (body.notice case final notice?)
              Padding(
                padding: const EdgeInsets.only(top: Space.sm),
                child: Text(
                  notice,
                  style: const TextStyle(color: KhadraColors.text, fontSize: 13, fontWeight: FontWeight.w700),
                ),
              ),
          ],
        ),
      );
}

class _Section extends StatelessWidget {
  const _Section({required this.section});

  final DocumentSectionView section;

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.only(bottom: Space.md),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Padding(
              padding: const EdgeInsets.only(bottom: Space.xs),
              child: Text(
                section.heading,
                style: const TextStyle(fontSize: 14, fontWeight: FontWeight.w800, color: KhadraColors.text),
              ),
            ),
            for (final block in section.blocks)
              switch (block) {
                LinesBlock(:final lines) => Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [for (final line in lines) _Line(line: line)],
                  ),
                AloneBlock(:final text, :final direction) => Padding(
                    padding: const EdgeInsets.symmetric(vertical: Space.xs),
                    // At the reading start, where a labelled value sits: given the
                    // whole width, a Latin literal would hug the LEFT edge of an
                    // Arabic page.
                    child: Align(
                      alignment: AlignmentDirectional.centerStart,
                      child: DocumentValueText(text, direction: direction, style: _valueStyle),
                    ),
                  ),
              },
          ],
        ),
      );
}

const _valueStyle = TextStyle(color: KhadraColors.text, fontSize: 13, fontWeight: FontWeight.w700, height: 1.4);

class _Line extends StatelessWidget {
  const _Line({required this.line});

  final DocumentLineView line;

  @override
  Widget build(BuildContext context) => DecoratedBox(
        decoration: const BoxDecoration(border: Border(bottom: BorderSide(color: KhadraColors.divider))),
        child: Padding(
          padding: const EdgeInsets.symmetric(vertical: 7),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(
                line.label,
                style: const TextStyle(color: KhadraColors.neutral600, fontSize: 12, fontWeight: FontWeight.w600),
              ),
              const SizedBox(height: 2),
              DocumentValueText(line.value, direction: line.direction, style: _valueStyle),
            ],
          ),
        ),
      );
}

/// A sentence that names a document number — "Issued against payment receipt
/// TEST-PAY-2026-000005" — laid out so the number is never broken across lines:
/// the words wrap as words and the number moves down whole, as the website's
/// `white-space: nowrap` keeps it. A number is what a customer quotes to support;
/// split at one of its hyphens it is two fragments.
class NumberedSentence extends StatelessWidget {
  const NumberedSentence(this.link, {super.key, this.style});

  final LinkText link;
  final TextStyle? style;

  @override
  Widget build(BuildContext context) {
    final parts = link.aroundNumber;
    if (parts == null) return Text(link.text, style: style);
    return Wrap(
      spacing: Space.xs,
      crossAxisAlignment: WrapCrossAlignment.center,
      children: [
        if (parts.before.isNotEmpty) Text(parts.before, style: style),
        NumberRun(number: link.number, after: parts.after, style: style),
      ],
    );
  }
}

/// A document number and whatever closes its sentence — a full stop in either
/// language — kept together as one piece.
class NumberRun extends StatelessWidget {
  const NumberRun({super.key, required this.number, this.after = '', this.style});

  final String number;
  final String after;
  final TextStyle? style;

  @override
  Widget build(BuildContext context) => after.isEmpty
      ? LatinRun(number, style: style)
      : Row(
          mainAxisSize: MainAxisSize.min,
          children: [LatinRun(number, style: style), Flexible(child: Text(after, style: style))],
        );
}

/// A value of a document, kept apart from the text around it by the reader's
/// rule: a Latin literal left to right ([LatinRun]), a name written in Arabic in
/// its own direction ([UserText]), and anything else as the stored text or the
/// formatter already isolated it.
class DocumentValueText extends StatelessWidget {
  const DocumentValueText(this.text, {super.key, required this.direction, this.style});

  final String text;
  final LiteralDirection? direction;
  final TextStyle? style;

  @override
  Widget build(BuildContext context) => switch (direction) {
        LiteralDirection.ltr => LatinRun(text, style: style),
        LiteralDirection.auto => UserText(text, style: style),
        null => Text(text, style: style),
      };
}
