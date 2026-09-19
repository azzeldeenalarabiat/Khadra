import 'dart:math' as math;

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/providers.dart';
import '../../core/router.dart';
import '../../core/theme/khadra_theme.dart';
import '../../core/widgets/khadra_widgets.dart';
import '../../core/widgets/language_menu.dart';
import '../../l10n/app_localizations.dart';

/// The app's front door, shown on a fresh install, after the app's data has been
/// cleared, and after a deliberate sign-out.
///
/// It exists because the app used to open on the catalogue, which says nothing
/// about whether there is an account, what one is for, or how to get one — so a
/// first-time customer discovered all three at the booking button. Three choices,
/// all of them visible, none of them dressed down to nudge somebody towards
/// another: browsing really is open, and a guest is not a second-class visitor.
///
/// **Nothing on it is invented.** No car count, no "from 15 JOD a day", no
/// promotion — the platform publishes no such figure, and a number typed into the
/// app's first screen would be the static-data rule broken on its front door. The
/// brand, one question, one sentence about what an account is for, and the three
/// buttons are the whole of it.
///
/// The design handoff draws no Get Started artboard. The hero is built from the
/// brand badge and the token file, and the choices sit on the same white, in the
/// same type, as the forms that two of them open.
class WelcomeScreen extends ConsumerWidget {
  const WelcomeScreen({super.key, this.next});

  /// Where the customer was heading when the app was launched.
  ///
  /// Only ever a route that needs an account: the router sends a destination to
  /// `/` solely when a GUARDED route was opened before the session had resolved.
  /// So it travels with the two buttons that can reach it and not with the third —
  /// offering to browse as a guest and then landing on somebody's own booking
  /// would only bounce them into the sign-in form they had just declined.
  final String? next;

  /// Below this height the hero gives up its breathing room, so all three choices
  /// are on the first screen of a small phone rather than below it. Set above the
  /// 640 of the smallest common phones, which need it most.
  static const double compactBelowHeight = 700;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final compact = MediaQuery.sizeOf(context).height < compactBelowHeight;

    return AnnotatedRegion<SystemUiOverlayStyle>(
      // Light status-bar icons, because they sit on the hero's green.
      value: SystemUiOverlayStyle.light,
      child: Scaffold(
        backgroundColor: KhadraColors.brandDeep,
        body: DecoratedBox(
          decoration: const BoxDecoration(gradient: KhadraGradients.hero),
          child: CustomScrollView(
            slivers: [
              SliverFillRemaining(
                // The hero takes whatever height the choices leave. Once the text is
                // too large for both to fit, the screen scrolls rather than squeezes.
                hasScrollBody: false,
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    Expanded(child: _Hero(compact: compact)),
                    _Choices(next: next, compact: compact),
                  ],
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

/// The badge, the name and the tagline, on the brand green, with the language
/// switch in the corner where a reader looks for it first.
class _Hero extends StatelessWidget {
  const _Hero({required this.compact});

  final bool compact;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final text = Theme.of(context).textTheme;

    return Stack(
      children: [
        Positioned.fill(
          // A drawing, not information: nothing in it for a screen reader.
          child: ExcludeSemantics(
            child: RepaintBoundary(
              child: CustomPaint(
                painter: _RoadPainter(Directionality.of(context)),
              ),
            ),
          ),
        ),
        SafeArea(
          bottom: false,
          child: Center(
            child: Padding(
              // On a short phone the brand starts level with the globe, which sits in
              // the corner rather than on a row of its own: that row was the height
              // of the third button, and the third button is what matters there.
              padding: EdgeInsets.fromLTRB(Space.xl, compact ? Space.md : Space.xxl * 2,
                  Space.xl, compact ? Space.lg : Space.xxl),
              child: Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  _Badge(size: compact ? 56 : 104),
                  SizedBox(height: compact ? Space.sm : Space.lg),
                  Text(
                    l10n.appName,
                    textAlign: TextAlign.center,
                    style: text.headlineMedium?.copyWith(
                      color: KhadraColors.onBrand,
                      fontSize: compact ? 24 : 32,
                    ),
                  ),
                  const SizedBox(height: Space.xs),
                  Text(
                    l10n.appTagline,
                    textAlign: TextAlign.center,
                    style: text.titleMedium?.copyWith(
                      color: KhadraColors.onBrandMuted,
                      fontWeight: FontWeight.w600,
                    ),
                  ),
                ],
              ),
            ),
          ),
        ),
        // Last, so it is drawn over the brand and is the first thing a tap reaches.
        const SafeArea(
          bottom: false,
          child: Align(
            alignment: AlignmentDirectional.topEnd,
            child: Padding(
              padding: EdgeInsetsDirectional.only(top: Space.xs, end: Space.sm),
              child: KhadraLanguageMenu(onBrand: true),
            ),
          ),
        ),
      ],
    );
  }
}

/// The brand badge, cut to its own circle.
///
/// The artwork is a round badge on a square of OPAQUE white, so without the cut it
/// would sit on the green as a white tile.
class _Badge extends StatelessWidget {
  const _Badge({required this.size});

  final double size;

  @override
  Widget build(BuildContext context) => DecoratedBox(
        decoration: const BoxDecoration(
          color: KhadraColors.onBrandFaint,
          shape: BoxShape.circle,
        ),
        child: Padding(
          padding: const EdgeInsets.all(Space.sm),
          // The name is read out once, from the heading under it.
          child: ExcludeSemantics(child: ClipOval(child: KhadraLogo(size: size))),
        ),
      );
}

/// The three ways in, on white.
class _Choices extends ConsumerWidget {
  const _Choices({required this.next, required this.compact});

  final String? next;
  final bool compact;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);
    final text = Theme.of(context).textTheme;

    return DecoratedBox(
      decoration: const BoxDecoration(
        color: KhadraColors.surface,
        borderRadius: Radii.sheetTop,
      ),
      child: SafeArea(
        top: false,
        child: Align(
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: Space.measure),
            child: Padding(
              padding: EdgeInsets.fromLTRB(
                  Space.xl, compact ? Space.xl : Space.xxl, Space.xl, Space.xl),
              child: Column(
                mainAxisSize: MainAxisSize.min,
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Text(l10n.welcomeTitle, style: text.headlineSmall),
                  const SizedBox(height: Space.sm),
                  Text(
                    l10n.welcomeBody,
                    style: text.bodyMedium?.copyWith(color: KhadraColors.neutral600),
                  ),
                  SizedBox(height: compact ? Space.lg : Space.xl),
                  FilledButton(
                    onPressed: () => _browse(context, ref),
                    child: Text(l10n.welcomeBrowseAsGuest),
                  ),
                  const SizedBox(height: Space.md),
                  _AccountButtons(next: next),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }

  /// Records the choice, then opens the app.
  ///
  /// The flag is written here and NOT when the other two buttons are tapped:
  /// tapping Sign in and then backing out is not a choice, and a customer who did
  /// that would otherwise find this screen gone on the next launch having never
  /// decided anything. The two forms set it themselves once they succeed.
  Future<void> _browse(BuildContext context, WidgetRef ref) async {
    await ref.read(entryChoiceProvider.notifier).choose();
    if (context.mounted) context.go(Routes.search);
  }
}

/// Sign in and Create account: side by side when each label fits its half, one
/// above the other when either does not.
///
/// Decided by MEASURING the two labels at the reader's own text size rather than by
/// a width breakpoint. "Create account" fits half of a 412 phone and not half of a
/// 360 one, the Arabic labels are shorter than the English, and a large system text
/// size changes all of it. A fixed pair of halves is what cropped the Arabic filter
/// label on the search screen.
class _AccountButtons extends StatelessWidget {
  const _AccountButtons({required this.next});

  final String? next;

  /// The room either side of each label. Stated here, rather than left to
  /// Material's default, so that the measurement below and the buttons agree
  /// about it.
  static const EdgeInsets _padding = EdgeInsets.symmetric(horizontal: Space.md);

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final style = OutlinedButton.styleFrom(padding: _padding);

    final signIn = OutlinedButton(
      style: style,
      // Pushed, not replaced, so the back gesture returns here rather than
      // leaving somebody on a form they did not choose with nowhere above it.
      onPressed: () => context.push(routeWithNext(Routes.signIn, next)),
      child: Text(l10n.authSignIn),
    );
    final register = OutlinedButton(
      style: style,
      onPressed: () => context.push(routeWithNext(Routes.register, next)),
      child: Text(l10n.authSignUp),
    );

    // The width this row really has: the screen, less any display cut-out and the
    // panel's own gutters, capped at the panel's measure. Read from MediaQuery, not
    // a LayoutBuilder, because this screen sizes itself by measuring its content
    // first, and a LayoutBuilder cannot be measured that way.
    final width = math.min(
      Space.measure,
      MediaQuery.sizeOf(context).width -
          MediaQuery.paddingOf(context).horizontal -
          2 * Space.xl,
    );
    final half = (width - Space.sm) / 2;

    if (_fits(context, l10n.authSignIn, half) &&
        _fits(context, l10n.authSignUp, half)) {
      return Row(
        children: [
          Expanded(child: signIn),
          const SizedBox(width: Space.sm),
          Expanded(child: register),
        ],
      );
    }

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [signIn, const SizedBox(height: Space.sm), register],
    );
  }

  /// Whether [label], set as an outlined button sets it, fits a button [width] wide.
  static bool _fits(BuildContext context, String label, double width) {
    final painter = TextPainter(
      text: TextSpan(
        text: label,
        style: DefaultTextStyle.of(context).style.merge(Theme.of(context)
            .outlinedButtonTheme
            .style
            ?.textStyle
            ?.resolve(const <WidgetState>{})),
      ),
      textDirection: Directionality.of(context),
      textScaler: MediaQuery.textScalerOf(context),
      maxLines: 1,
    )..layout();
    final needed = painter.width;
    painter.dispose();

    // The label, the padding either side of it, and a little air for the border
    // and for rounding, so a label that only just fits does not wrap after all.
    return needed + _padding.horizontal + Space.sm <= width;
  }
}

/// A road behind the brand: two broad lanes sweeping toward the reading end, and
/// the dashed centre line of one of them.
///
/// Faint on purpose. It is there so the hero reads as a journey rather than a green
/// rectangle; anything stronger would sit between the reader and the name.
class _RoadPainter extends CustomPainter {
  const _RoadPainter(this.direction);

  final TextDirection direction;

  @override
  void paint(Canvas canvas, Size size) {
    if (size.isEmpty) return;

    // Drawn for left-to-right and turned over for Arabic, so the road runs the way
    // the page is read.
    if (direction == TextDirection.rtl) {
      canvas
        ..translate(size.width, 0)
        ..scale(-1, 1);
    }

    final w = size.width;
    final h = size.height;

    Path lane(double lift) => Path()
      ..moveTo(-0.1 * w, h * (1.02 - lift))
      ..cubicTo(0.3 * w, h * (0.78 - lift), 0.55 * w, h * (0.98 - lift),
          1.1 * w, h * (0.52 - lift));

    final road = lane(0);
    final lanes = Paint()
      ..color = KhadraColors.onBrandFaint
      ..style = PaintingStyle.stroke
      ..strokeCap = StrokeCap.round;

    canvas
      ..drawPath(road, lanes..strokeWidth = math.max(28.0, w * 0.09))
      ..drawPath(lane(0.34), lanes..strokeWidth = math.max(18.0, w * 0.05));

    final centreLine = Paint()
      ..color = KhadraColors.onBrandLine
      ..style = PaintingStyle.stroke
      ..strokeCap = StrokeCap.round
      ..strokeWidth = 2.5;

    const dash = 14.0;
    const gap = 12.0;
    for (final metric in road.computeMetrics()) {
      for (var at = 0.0; at < metric.length; at += dash + gap) {
        canvas.drawPath(
          metric.extractPath(at, math.min(at + dash, metric.length)),
          centreLine,
        );
      }
    }
  }

  @override
  bool shouldRepaint(_RoadPainter oldDelegate) => oldDelegate.direction != direction;
}
