import 'package:cached_network_image/cached_network_image.dart';
import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
// `show Bidi`: intl exports a TextDirection of its own, which shadows the
// framework's and makes `TextDirection.rtl` stop resolving.
import 'package:intl/intl.dart' show Bidi;

import '../../api/dtos.dart' show ResolvedText;
import '../../l10n/app_localizations.dart';
import '../theme/khadra_theme.dart';

/// The Khadra mark.
///
/// One asset, the same one the console serves, so the three surfaces cannot drift
/// into three logos. It is a raster image with its own padding and colour, so it
/// is never tinted or recoloured here.
class KhadraLogo extends StatelessWidget {
  const KhadraLogo({super.key, this.size = 48});

  final double size;

  @override
  Widget build(BuildContext context) => Image.asset(
        'assets/brand/khadra-logo.png',
        width: size,
        height: size,
        fit: BoxFit.contain,
        filterQuality: FilterQuality.medium,
        semanticLabel: AppLocalizations.of(context).appName,
      );
}

/// The wordmark under the logo, for the screens that introduce the app.
class KhadraWordmark extends StatelessWidget {
  const KhadraWordmark({super.key, this.logoSize = 96});

  final double logoSize;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return Column(
      mainAxisSize: MainAxisSize.min,
      children: [
        KhadraLogo(size: logoSize),
        const SizedBox(height: Space.md),
        Text(
          l10n.appTagline,
          textAlign: TextAlign.center,
          style: const TextStyle(color: KhadraColors.neutral600, fontSize: 15),
        ),
      ],
    );
  }
}

/// Leaves the current screen, whether or not anything pushed it.
///
/// `context.pop()` on its own THROWS `GoError: There is nothing to pop` when the
/// screen is the only page on the stack — which is not an edge case here. Every
/// account screen is deep-linkable, and signing in on the way to one replaces the
/// stack rather than adding to it: open `/profile/password` from a cold start,
/// sign in, change the password, and the success path was the thing that crashed.
///
/// So: pop when there is something to pop, and otherwise go to [fallback].
void khadraLeave(BuildContext context, String fallback) {
  if (context.canPop()) {
    context.pop();
  } else {
    context.go(fallback);
  }
}

/// A back button that always leads somewhere.
///
/// Every screen here can be reached with an EMPTY stack — from a notification, a
/// link in an email, or a flow that replaced the stack on its way in. `BackButton`
/// renders nothing at all in that case, which leaves a customer on a pushed screen
/// with no navigation and no tabs: on the web there is no way out but the browser's
/// own back, and on a phone the gesture quits the app.
/// The title of a screen that is somewhere you ARRIVE, not somewhere you opened.
///
/// The design has two title sizes and one rule for choosing: a screen you can go
/// back from wears 16, and a screen that is the root of a tab wears 20. The rule
/// is about the back arrow, not about which screen it is — so Saved cars, which
/// the handoff draws as a tab at 20, is 16 here because this app reaches it from
/// Profile and it has an arrow.
///
/// 20 fits at 375 with room to spare; the app bar has no leading control on these
/// screens, which is the whole reason the design can afford the larger size.
class KhadraLargeTitle extends StatelessWidget {
  const KhadraLargeTitle(this.text, {super.key});

  final String text;

  @override
  Widget build(BuildContext context) => Text(
        text,
        style: TextStyle(
          fontSize: 20,
          fontWeight: FontWeight.w800,
          letterSpacing: KhadraType.of(context, -0.4),
          color: KhadraColors.text,
        ),
        maxLines: 1,
        overflow: TextOverflow.ellipsis,
      );
}

class KhadraBack extends StatelessWidget {
  const KhadraBack({super.key, required this.fallback, this.onSurface = false});

  final String fallback;

  /// Sitting over a PHOTOGRAPH rather than on a bar, so it needs its own white
  /// disc: a bare dark chevron disappears into the first car photographed at
  /// night, and the save button beside it already has one.
  final bool onSurface;

  @override
  Widget build(BuildContext context) {
    final button = _button(context);
    if (!onSurface) return button;

    return DecoratedBox(
      decoration: BoxDecoration(
        color: KhadraColors.surface.withValues(alpha: 0.92),
        shape: BoxShape.circle,
      ),
      child: button,
    );
  }

  Widget _button(BuildContext context) => IconButton(
        // A plain CHEVRON, which is what the design draws, rather than Material's
        // arrow-with-a-shaft. It is on fifteen screens, it is the single most
        // repeated glyph in the app, and it is one icon to change.
        //
        // NAMED ONCE, and not chosen by direction. `Icons.chevron_left` carries
        // `matchTextDirection: true`, and the `Icon` widget mirrors any such glyph
        // under an RTL `Directionality` itself — so this already points right in
        // Arabic. Picking `chevron_right` for Arabic by hand, as this did, mirrors
        // a glyph that was about to be mirrored anyway and lands back where it
        // started: a back button pointing LEFT on every Arabic screen.
        //
        // `KhadraDisclosure` below is the same rule for the trailing chevron, and
        // `rtl_audit_test` forbids the hand-written version so this cannot come
        // back.
        icon: Icon(Icons.chevron_left, size: 26),
        tooltip: MaterialLocalizations.of(context).backButtonTooltip,
        onPressed: () => khadraLeave(context, fallback),
      );
}

/// A network image with a placeholder that is never a broken-image glyph.
///
/// Null is a real answer from this API: publishing a car demands a photo, but
/// removing one never re-checks that, so an active car can legitimately have none.
class KhadraImage extends StatelessWidget {
  const KhadraImage({
    super.key,
    required this.url,
    this.fit = BoxFit.cover,
    this.borderRadius,
  });

  final String? url;
  final BoxFit fit;
  final BorderRadius? borderRadius;

  @override
  Widget build(BuildContext context) {
    final radius = borderRadius ?? BorderRadius.zero;
    final address = url;

    if (address == null || address.isEmpty) {
      return ClipRRect(borderRadius: radius, child: const _ImagePlaceholder());
    }

    return ClipRRect(
      borderRadius: radius,
      child: CachedNetworkImage(
        imageUrl: address,
        fit: fit,
        fadeInDuration: const Duration(milliseconds: 150),
        placeholder: (_, __) => const _ImagePlaceholder(spinning: true),
        errorWidget: (_, __, ___) => const _ImagePlaceholder(),
      ),
    );
  }
}

class _ImagePlaceholder extends StatelessWidget {
  const _ImagePlaceholder({this.spinning = false});

  final bool spinning;

  @override
  Widget build(BuildContext context) => ColoredBox(
        // The design's own empty-frame grey, a hair off the chip fill beside it so a
        // missing photograph reads as a gap rather than as a surface.
        color: KhadraColors.imagePlaceholder,
        child: Center(
          child: spinning
              ? const SizedBox(
                  width: 20,
                  height: 20,
                  child: CircularProgressIndicator(strokeWidth: 2),
                )
              : const Icon(
                  Icons.directions_car_outlined,
                  color: KhadraColors.neutral400,
                  size: 32,
                ),
        ),
      );
}

/// What a screen shows while it has nothing yet.
class KhadraLoading extends StatelessWidget {
  const KhadraLoading({super.key, this.compact = false});

  final bool compact;

  @override
  Widget build(BuildContext context) => Padding(
        padding: EdgeInsets.symmetric(vertical: compact ? Space.lg : Space.xxl * 2),
        child: const Center(child: CircularProgressIndicator()),
      );
}

/// What a screen shows when the request failed.
///
/// Always with a way out. An error with no retry is a dead end, and on a phone
/// that means force-quitting the app.
class KhadraError extends StatelessWidget {
  const KhadraError({super.key, required this.message, this.onRetry});

  final String message;
  final VoidCallback? onRetry;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return Padding(
      padding: const EdgeInsets.all(Space.xl),
      child: Column(
        mainAxisAlignment: MainAxisAlignment.center,
        children: [
          const Icon(Icons.cloud_off_outlined,
              size: 44, color: KhadraColors.neutral400),
          const SizedBox(height: Space.lg),
          Text(
            message,
            textAlign: TextAlign.center,
            style: const TextStyle(color: KhadraColors.neutral700, fontSize: 15),
          ),
          if (onRetry != null) ...[
            const SizedBox(height: Space.lg),
            OutlinedButton(onPressed: onRetry, child: Text(l10n.actionRetry)),
          ],
        ],
      ),
    );
  }
}

/// An empty state that SAYS what would be here, rather than showing nothing.
class KhadraEmpty extends StatelessWidget {
  const KhadraEmpty({
    super.key,
    required this.icon,
    required this.title,
    this.body,
    this.action,
  });

  final IconData icon;
  final String title;
  final String? body;
  final Widget? action;

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.symmetric(
            horizontal: Space.xl, vertical: Space.xxl + Space.lg),
        child: Column(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            // The icon sits IN something. A bare 52px glyph floating above the
            // text reads as a failure; the design's rounded grey tile reads as a
            // place where something will be.
            Container(
              width: 54,
              height: 54,
              decoration: const BoxDecoration(
                color: KhadraColors.neutral100,
                borderRadius: Radii.card,
              ),
              child: Icon(icon, size: 24, color: KhadraColors.neutral500),
            ),
            const SizedBox(height: Space.lg),
            Text(
              title,
              textAlign: TextAlign.center,
              style: const TextStyle(
                fontSize: 16,
                fontWeight: FontWeight.w800,
                color: KhadraColors.text,
              ),
            ),
            if (body != null) ...[
              const SizedBox(height: 7),
              Text(
                body!,
                textAlign: TextAlign.center,
                style: const TextStyle(
                  color: KhadraColors.neutral600,
                  fontSize: 13,
                  fontWeight: FontWeight.w500,
                  height: 1.5,
                ),
              ),
            ],
            if (action != null) ...[
              const SizedBox(height: 18),
              action!,
            ],
          ],
        ),
      );
}

/// A card, in the platform's geometry.
class KhadraCard extends StatelessWidget {
  const KhadraCard({
    super.key,
    required this.child,
    this.padding = const EdgeInsets.all(Space.card),
    this.onTap,
    this.borderColor,
    this.background,
    this.borderRadius = Radii.card,
  });

  final Widget child;
  final EdgeInsetsGeometry padding;
  final VoidCallback? onTap;
  final Color? borderColor;
  final Color? background;

  /// `Radii.card` by default; `Radii.row` for a list of papers or of a gallery's
  /// cars, which the design draws a step tighter.
  final BorderRadius borderRadius;

  @override
  Widget build(BuildContext context) {
    final body = Container(
      decoration: BoxDecoration(
        color: background ?? KhadraColors.surface,
        borderRadius: borderRadius,
        border: Border.all(color: borderColor ?? KhadraColors.neutral200),
        // The design's single shadow, and it is almost nothing: surfaces are
        // separated by the border, and this only lifts the card a hair off the page.
        boxShadow: Shadows.card,
      ),
      padding: padding,
      child: child,
    );

    if (onTap == null) return body;

    return Material(
      color: Colors.transparent,
      child: InkWell(
        onTap: onTap,
        borderRadius: borderRadius,
        child: body,
      ),
    );
  }
}

/// A short, coloured label. Used for booking status and for anything that needs to
/// read at a glance.
class KhadraBadge extends StatelessWidget {
  const KhadraBadge({
    super.key,
    required this.label,
    required this.colour,
    this.icon,
  });

  final String label;
  final Color colour;
  final IconData? icon;

  @override
  Widget build(BuildContext context) => Container(
        padding: const EdgeInsetsDirectional.only(
          start: Space.sm,
          end: Space.sm,
          top: 4,
          bottom: 4,
        ),
        // A SOFT-cornered rectangle filled with a wash of its own colour, and no
        // border. The design carries state on the fill alone; an outline as well
        // turns a label into a second button on a card that already has one.
        decoration: BoxDecoration(
          color: colour.withValues(alpha: 0.12),
          borderRadius: Radii.pill,
        ),
        child: Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            if (icon != null) ...[
              Icon(icon, size: 11, color: colour),
              const SizedBox(width: 4),
            ],
            Flexible(
              child: Text(
                label,
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: TextStyle(
                  color: colour,
                  fontSize: 10,
                  fontWeight: FontWeight.w700,
                  letterSpacing: KhadraType.of(context, 0.3),
                ),
              ),
            ),
          ],
        ),
      );
}

/// A row of a key and a value, the shape most of this app's detail screens are
/// made of.
class KhadraDetailRow extends StatelessWidget {
  const KhadraDetailRow({
    super.key,
    required this.label,
    required this.value,
    this.valueStyle,
    this.dense = false,
  });

  final String label;
  final Widget value;
  final TextStyle? valueStyle;
  final bool dense;

  @override
  Widget build(BuildContext context) => Padding(
        padding: EdgeInsets.symmetric(vertical: dense ? 4 : 7),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Expanded(
              child: Text(
                label,
                style: const TextStyle(
                  color: KhadraColors.neutral600,
                  fontSize: 13,
                  fontWeight: FontWeight.w600,
                ),
              ),
            ),
            const SizedBox(width: Space.md),
            DefaultTextStyle.merge(
              style: valueStyle ??
                  const TextStyle(
                    color: KhadraColors.text,
                    fontSize: 13,
                    fontWeight: FontWeight.w700,
                  ),
              textAlign: TextAlign.end,
              child: value,
            ),
          ],
        ),
      );
}

/// The design's choice chip: a bordered rounded rectangle that tints and takes
/// an accent outline when it is the one chosen.
///
/// Drawn rather than themed from Material's `Chip`. Material sizes a chip's label
/// box from the style's own metrics and clips what does not fit, and Noto Kufi
/// Arabic's line box is deeper than Manrope's at the same point size -- so the
/// city chips came out with the top and bottom sliced off every Arabic word
/// while the English ones looked fine. A Container has no opinion about the
/// script inside it.
class KhadraChoiceChip extends StatelessWidget {
  const KhadraChoiceChip({
    super.key,
    required this.label,
    required this.onTap,
    this.selected = false,
  });

  final String label;
  final VoidCallback onTap;
  final bool selected;

  static const double _fontSize = 12;
  static const double _padding = 9;
  static const double _border = 1;

  /// The height one of these takes at the reader's chosen text size.
  ///
  /// Exposed because one caller has to know BEFORE it builds one: a horizontal
  /// list must be given a height, and the number that was written down there —
  /// 42 — was measured in English at the default size. Arabic's line box is
  /// deeper and a customer who has turned text up gets more of both, so a
  /// constant is clipped in exactly the cases nobody takes a screenshot of.
  ///
  /// It reads the SAME constants the chip builds with, so the two cannot drift
  /// the day the padding or the point size changes.
  static double heightIn(BuildContext context) {
    final ambient = DefaultTextStyle.of(context);

    final painter = TextPainter(
      // BOTH scripts in one line, and that is the point. A run of Latin is set in
      // Manrope and a run of Arabic falls through to Noto Kufi Arabic, the two
      // faces do not have the same metrics, and a line takes the taller of the
      // runs it contains. Measuring either alphabet alone under-reports the other
      // by a pixel or two — which is all it takes to shave the top off a word.
      text: TextSpan(text: 'Aع', style: _styleIn(context, selected: true)),
      // rtl-audit: allow — measuring a sample, not laying out a screen.
      textDirection: TextDirection.ltr,
      textScaler: MediaQuery.textScalerOf(context),
      // The same height behaviour `Text` will be laid out with. Left to the
      // default the painter and the widget can disagree, and the painter is the
      // one that says the row is big enough. (No strut: the chip's `Text` sets
      // none either, so the line height comes from the runs in both.)
      textHeightBehavior: ambient.textHeightBehavior,
    )..layout();

    final height = painter.height;
    painter.dispose();

    // Ceiled: a paragraph is laid out in whole pixels and a row reserving 36.4 for
    // a chip that takes 37 clips it by the rounding alone.
    return height.ceilToDouble() + (_padding + _border) * 2;
  }

  /// Resolved against the ambient default so the measurement runs in the app's
  /// own faces. A bare `TextStyle` names no family and would be measured in the
  /// platform's, which is not what any of this renders in.
  static TextStyle _styleIn(BuildContext context, {required bool selected}) =>
      DefaultTextStyle.of(context).style.merge(
            TextStyle(
              fontSize: _fontSize,
              fontWeight: selected ? FontWeight.w700 : FontWeight.w600,
              color: selected ? KhadraColors.price : KhadraColors.neutral800,
            ),
          );

  @override
  Widget build(BuildContext context) => Material(
        color: selected ? KhadraColors.accent100 : KhadraColors.surface,
        borderRadius: Radii.chip,
        child: InkWell(
          onTap: onTap,
          borderRadius: Radii.chip,
          child: Container(
            padding: const EdgeInsets.symmetric(
                horizontal: 13, vertical: _padding),
            decoration: BoxDecoration(
              borderRadius: Radii.chip,
              border: Border.all(
                width: _border,
                color:
                    selected ? KhadraColors.accent : KhadraColors.neutral300,
              ),
            ),
            child: Text(label, style: _styleIn(context, selected: selected)),
          ),
        ),
      );
}

/// The chevron at the end of a row you can open.
///
/// It points the way the language runs — right in English, left in Arabic — and
/// FLUTTER does that, not this widget. `Icons.chevron_right` is declared with
/// `matchTextDirection: true`, and `Icon` flips any such glyph horizontally under
/// an RTL `Directionality`. So the four screens that named it directly were
/// right; what was wrong was the correction, which chose `chevron_left` for
/// Arabic and had it mirrored straight back into pointing right.
///
/// This exists for the colour and for one place to look, not to pick the glyph.
/// `rtl_audit_test` forbids picking one by hand anywhere.
class KhadraDisclosure extends StatelessWidget {
  const KhadraDisclosure({super.key, this.color});

  final Color? color;

  @override
  Widget build(BuildContext context) => Icon(
        Icons.chevron_right,
        color: color ?? KhadraColors.neutral400,
      );
}

/// The small label above ONE control -- a group of chips, a slider, a field.
///
/// Deliberately not a section title: inside a sheet the design drops to a quiet
/// 12, because the sheet's own heading is already doing the shouting and a column
/// of full-weight headings makes six controls look like six screens.
class KhadraFieldLabel extends StatelessWidget {
  const KhadraFieldLabel(this.label, {super.key});

  final String label;

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.only(bottom: 10),
        child: Text(
          label,
          style: const TextStyle(
            fontSize: 12,
            fontWeight: FontWeight.w700,
            color: KhadraColors.neutral700,
          ),
        ),
      );
}

/// The design's SPECIFICATIONS block: a two-column grid of small bordered
/// tiles, each an uppercase label over its value.
///
/// A list of label/value rows says the same words, but a car's specification is
/// six unrelated facts of the same weight, and a column makes the first one look
/// like the heading for the rest.
class KhadraSpecGrid extends StatelessWidget {
  const KhadraSpecGrid({super.key, required this.specs});

  final List<({String label, String value})> specs;

  @override
  Widget build(BuildContext context) => LayoutBuilder(
        builder: (context, constraints) {
          const gap = 10.0;
          final width = (constraints.maxWidth - gap) / 2;

          return Wrap(
            spacing: gap,
            runSpacing: gap,
            children: [
              for (final spec in specs)
                SizedBox(
                  width: width,
                  child: Container(
                    padding: const EdgeInsets.symmetric(
                        horizontal: Space.md, vertical: 11),
                    decoration: BoxDecoration(
                      color: KhadraColors.surface,
                      borderRadius: Radii.field,
                      border: Border.all(color: KhadraColors.neutral200),
                    ),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          spec.label.toUpperCase(),
                          style: TextStyle(
                            fontSize: 10,
                            fontWeight: FontWeight.w600,
                            letterSpacing: KhadraType.of(context, 0.5),
                            color: KhadraColors.neutral500,
                          ),
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                        ),
                        const SizedBox(height: 2),
                        Text(
                          spec.value,
                          style: const TextStyle(
                              fontSize: 13, fontWeight: FontWeight.w700),
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                        ),
                      ],
                    ),
                  ),
                ),
            ],
          );
        },
      );
}

/// A heading above a group of rows.
class KhadraSectionTitle extends StatelessWidget {
  const KhadraSectionTitle(this.title, {super.key, this.trailing});

  final String title;
  final Widget? trailing;

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.only(bottom: Space.md),
        child: Row(
          children: [
            Expanded(
              child: Text(
                title,
                style: TextStyle(
                  fontSize: 15,
                  fontWeight: FontWeight.w800,
                  color: KhadraColors.text,
                  letterSpacing: KhadraType.of(context, -0.2),
                ),
              ),
            ),
            if (trailing != null) trailing!,
          ],
        ),
      );
}

/// A quiet block of explanation, for the places where the app has to be honest
/// about something rather than show a control that cannot work.
class KhadraNotice extends StatelessWidget {
  const KhadraNotice({
    super.key,
    required this.title,
    this.body,
    this.tone = NoticeTone.neutral,
    this.icon,
    this.action,
  });

  final String title;
  final String? body;
  final NoticeTone tone;
  final IconData? icon;
  final Widget? action;

  @override
  Widget build(BuildContext context) {
    final colour = switch (tone) {
      NoticeTone.neutral => KhadraColors.neutral700,
      NoticeTone.accent => KhadraColors.accent,
      NoticeTone.warn => KhadraColors.warn,
      NoticeTone.bad => KhadraColors.bad,
    };

    return Container(
      width: double.infinity,
      padding: const EdgeInsets.all(Space.lg),
      decoration: BoxDecoration(
        color: colour.withValues(alpha: 0.06),
        borderRadius: Radii.card,
        border: Border.all(color: colour.withValues(alpha: 0.22)),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Icon(icon ?? _defaultIcon(tone), size: 18, color: colour),
              const SizedBox(width: Space.sm),
              Expanded(
                child: Text(
                  title,
                  style: TextStyle(
                    fontSize: 15,
                    fontWeight: FontWeight.w600,
                    color: colour,
                    height: 1.35,
                  ),
                ),
              ),
            ],
          ),
          if (body != null) ...[
            const SizedBox(height: Space.sm),
            Text(
              body!,
              style: const TextStyle(
                fontSize: 14,
                height: 1.5,
                color: KhadraColors.neutral700,
              ),
            ),
          ],
          if (action != null) ...[
            const SizedBox(height: Space.md),
            action!,
          ],
        ],
      ),
    );
  }

  static IconData _defaultIcon(NoticeTone tone) => switch (tone) {
        NoticeTone.neutral => Icons.info_outline,
        NoticeTone.accent => Icons.check_circle_outline,
        NoticeTone.warn => Icons.schedule_outlined,
        NoticeTone.bad => Icons.error_outline,
      };
}

enum NoticeTone { neutral, accent, warn, bad }

/// A star rating. Read-only unless [onChanged] is given.
class KhadraStars extends StatelessWidget {
  const KhadraStars({
    super.key,
    required this.rating,
    this.size = 16,
    this.onChanged,
  });

  /// Null renders empty outlines: nobody has rated this, which is different from a
  /// zero-star score and must not look like one.
  final num? rating;
  final double size;
  final ValueChanged<int>? onChanged;

  @override
  Widget build(BuildContext context) {
    final value = rating ?? 0;
    return Row(
      mainAxisSize: MainAxisSize.min,
      children: List.generate(5, (index) {
        final position = index + 1;
        final filled = value >= position;
        final half = !filled && value > index && value < position;

        final star = Icon(
          filled
              ? Icons.star_rounded
              : half
                  ? Icons.star_half_rounded
                  : Icons.star_outline_rounded,
          size: size,
          color: rating == null ? KhadraColors.neutral300 : KhadraColors.star,
        );

        if (onChanged == null) return star;

        return IconButton(
          onPressed: () => onChanged!(position),
          icon: star,
          padding: const EdgeInsets.all(2),
          constraints: const BoxConstraints(),
          visualDensity: VisualDensity.compact,
        );
      }),
    );
  }
}

/// Keeps a Latin run upright inside Arabic text.
///
/// A booking reference (`KH-3M7QK2PA`), a plate number, a `+962` phone number and
/// an email are all Latin, and dropping one into an RTL paragraph without an
/// isolate lets the bidi algorithm reorder its neighbours — a reference reads
/// backwards, a phone number puts its `+` at the wrong end. The console solved the
/// same problem with an `.ltr` class.
class LatinRun extends StatelessWidget {
  const LatinRun(this.text, {super.key, this.style});

  final String text;
  final TextStyle? style;

  @override
  Widget build(BuildContext context) => Directionality(
        // rtl-audit: allow — an isolate is exactly what this widget is for.
        textDirection: TextDirection.ltr,
        child: Text(text, style: style),
      );
}

/// Text somebody TYPED, laid out in the direction they typed it in.
///
/// A gallery writes its description in Arabic or in English, a customer writes a
/// dispute statement in either, and the interface language says nothing about
/// which. Rendering an English paragraph inside an Arabic layout puts its full
/// stop at the wrong end — the gallery page showed ".open seven days a week",
/// with the period orphaned at the start of the line — and an Arabic paragraph in
/// an English layout has the mirror-image problem.
///
/// The direction comes from the TEXT, through the bidi algorithm's own
/// first-strong rule, and the alignment follows it so the paragraph does not sit
/// ragged against the wrong margin. Text with no strong character either way — a
/// number, a plate — keeps the interface direction, which is the right default
/// for something that reads the same both ways.
///
/// This is NOT for platform copy. Every string from the ARB files is written in
/// the language it will be read in, and belongs in an ordinary [Text].
class UserText extends StatelessWidget {
  const UserText(this.text, {super.key, this.style, this.maxLines, this.language});

  /// The same paragraph out of a [ResolvedText], carrying the language the SERVER
  /// says the office wrote it in.
  ///
  /// The point of the named constructor is that the pair travels together: a
  /// caller unpacking `.text` on its own would drop the language silently, and
  /// nothing on screen would look wrong.
  UserText.resolved(ResolvedText resolved, {super.key, this.style, this.maxLines})
      : text = resolved.text,
        language = resolved.language;

  final String text;
  final TextStyle? style;
  final int? maxLines;

  /// `ar` or `en`, when the server said which language this is. Null for text
  /// whose language nothing has claimed — a customer's own dispute statement.
  ///
  /// **Not used for direction**, which comes from the characters below and is the
  /// more reliable of the two: an office may type Arabic into the English box, and
  /// the paragraph still has to read correctly. It sets the text's LOCALE, so a
  /// screen reader given an English fallback paragraph inside an Arabic app reads
  /// it as English rather than sounding out Latin letters in Arabic.
  final String? language;

  /// The direction [text] will be laid out in.
  ///
  /// Exposed because a caller that has to MEASURE this paragraph — the fold
  /// behind a "Show more" — must measure it the way it is rendered, and a second
  /// copy of the detection rule is a second rule.
  static TextDirection directionOf(String text) =>
      // rtl-audit: allow — derived from the typed text, not from the interface.
      Bidi.detectRtlDirectionality(text) ? TextDirection.rtl : TextDirection.ltr;

  @override
  Widget build(BuildContext context) {
    final direction = directionOf(text);

    return Text(
      text,
      style: style,
      maxLines: maxLines,
      overflow: maxLines == null ? null : TextOverflow.ellipsis,
      // rtl-audit: allow — the direction comes from the TEXT, not the interface.
      textDirection: direction,
      // Start, not left. It resolves against the direction set on the line above,
      // which is the typed text's own — so this reads from the paragraph's leading
      // edge whichever way that paragraph runs, and needs no second branch.
      textAlign: TextAlign.start,
      // The office's own claim about the language, for the reading voice. Font
      // choice does not need it — Flutter's fallback is per GLYPH, so Arabic
      // letters reach Noto Kufi Arabic whatever locale is set — and direction must
      // not use it, for the reason on the field.
      locale: language == null ? null : Locale(language!),
    );
  }
}

/// Shows a message without stacking snack bars on top of each other.
void showKhadraMessage(
  BuildContext context,
  String message, {
  bool isError = false,
  /// Override the default only where the message describes a window the caller
  /// is counting — the Back-to-exit hint, which must not outlive the two seconds
  /// it is offering.
  Duration? duration,
}) =>
    showKhadraMessageOn(ScaffoldMessenger.of(context), message,
        isError: isError, duration: duration);

/// [showKhadraMessage] for a caller whose own context may be gone by the time it
/// has something to say -- a widget that awaited a pushed route and a re-read,
/// during which the screen can rebuild it away. Take the messenger first, say it
/// later.
void showKhadraMessageOn(
  ScaffoldMessengerState messenger,
  String message, {
  bool isError = false,
  Duration? duration,
}) {
  messenger
    ..hideCurrentSnackBar()
    ..showSnackBar(
      SnackBar(
        content: Text(message),
        backgroundColor: isError ? KhadraColors.bad : KhadraColors.neutral900,
        duration: duration ?? Duration(seconds: isError ? 5 : 3),
      ),
    );
}
