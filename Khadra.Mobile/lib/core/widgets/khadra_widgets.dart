import 'package:cached_network_image/cached_network_image.dart';
import 'package:flutter/material.dart';

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
        color: KhadraColors.neutral100,
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
        padding: const EdgeInsets.all(Space.xl),
        child: Column(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            Icon(icon, size: 52, color: KhadraColors.neutral300),
            const SizedBox(height: Space.lg),
            Text(
              title,
              textAlign: TextAlign.center,
              style: const TextStyle(
                fontSize: 18,
                fontWeight: FontWeight.w600,
                color: KhadraColors.text,
              ),
            ),
            if (body != null) ...[
              const SizedBox(height: Space.sm),
              Text(
                body!,
                textAlign: TextAlign.center,
                style: const TextStyle(
                    color: KhadraColors.neutral600, fontSize: 15, height: 1.45),
              ),
            ],
            if (action != null) ...[
              const SizedBox(height: Space.xl),
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
    this.padding = const EdgeInsets.all(Space.lg),
    this.onTap,
    this.borderColor,
    this.background,
  });

  final Widget child;
  final EdgeInsetsGeometry padding;
  final VoidCallback? onTap;
  final Color? borderColor;
  final Color? background;

  @override
  Widget build(BuildContext context) {
    final body = Container(
      decoration: BoxDecoration(
        color: background ?? KhadraColors.surface,
        borderRadius: Radii.card,
        border: Border.all(color: borderColor ?? KhadraColors.neutral200),
      ),
      padding: padding,
      child: child,
    );

    if (onTap == null) return body;

    return Material(
      color: Colors.transparent,
      child: InkWell(
        onTap: onTap,
        borderRadius: Radii.card,
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
          top: 5,
          bottom: 5,
        ),
        decoration: BoxDecoration(
          color: colour.withValues(alpha: 0.10),
          borderRadius: Radii.chip,
          border: Border.all(color: colour.withValues(alpha: 0.28)),
        ),
        child: Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            if (icon != null) ...[
              Icon(icon, size: 13, color: colour),
              const SizedBox(width: 4),
            ],
            Text(
              label,
              style: TextStyle(
                color: colour,
                fontSize: 12,
                fontWeight: FontWeight.w600,
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
                    color: KhadraColors.neutral600, fontSize: 14),
              ),
            ),
            const SizedBox(width: Space.md),
            DefaultTextStyle.merge(
              style: valueStyle ??
                  const TextStyle(
                    color: KhadraColors.text,
                    fontSize: 14,
                    fontWeight: FontWeight.w600,
                  ),
              textAlign: TextAlign.end,
              child: value,
            ),
          ],
        ),
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
                style: const TextStyle(
                  fontSize: 16,
                  fontWeight: FontWeight.w600,
                  color: KhadraColors.text,
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
          color: rating == null ? KhadraColors.neutral300 : KhadraColors.warn,
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
        textDirection: TextDirection.ltr,
        child: Text(text, style: style),
      );
}

/// Shows a message without stacking snack bars on top of each other.
void showKhadraMessage(BuildContext context, String message, {bool isError = false}) {
  ScaffoldMessenger.of(context)
    ..hideCurrentSnackBar()
    ..showSnackBar(
      SnackBar(
        content: Text(message),
        backgroundColor: isError ? KhadraColors.bad : KhadraColors.neutral900,
        duration: Duration(seconds: isError ? 5 : 3),
      ),
    );
}
