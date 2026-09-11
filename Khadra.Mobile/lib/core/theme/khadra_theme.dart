import 'package:flutter/material.dart';

/// The Khadra palette, taken from the approved customer-app design handoff
/// (`KHADRA Customer App.dc.html`), which the owner made the visual source of
/// truth for this app on 2026-09-11.
///
/// Before that it was a port of `Khadra.Dashboard/src/styles/_tokens.scss`, on the
/// reasoning that one platform should not have two greens. That reasoning still
/// holds and the answer simply moved: the handoff is now the one the app follows,
/// and the console keeps its own until somebody decides otherwise. Anything that
/// changes here changes there too, or the two drift apart again.
///
/// **Nothing in this app names a colour outside this file.** Not one
/// `Color(0x…)`, not one screen-specific green. That is what made adopting the
/// handoff a change to a token list rather than a sweep through thirty screens,
/// and it is worth keeping true.
abstract final class KhadraColors {
  static const Color background = Color(0xFFF6F7F6);
  static const Color surface = Color(0xFFFFFFFF);
  static const Color text = Color(0xFF111827);

  /// The brand green. Every button, every heart, every active tab.
  static const Color accent = Color(0xFF15803D);
  static const Color accentBright = Color(0xFF16A34A);

  /// The DARKER green the handoff reserves for money. A price is the thing on a
  /// card a reader looks for first, and it is not an action.
  static const Color price = Color(0xFF14532D);

  /// Greys, in the handoff's own steps. `neutral600` is the one that matters most:
  /// it is the muted line under a title on nearly every card in the design.
  static const Color neutral100 = Color(0xFFF4F6F4);
  static const Color neutral200 = Color(0xFFE8EAE9);
  static const Color neutral300 = Color(0xFFE2E5E3);
  static const Color neutral400 = Color(0xFFD1D5DB);
  static const Color neutral500 = Color(0xFF9CA3AF);
  static const Color neutral600 = Color(0xFF6B7280);
  static const Color neutral700 = Color(0xFF4B5563);
  static const Color neutral800 = Color(0xFF374151);
  static const Color neutral900 = Color(0xFF111827);

  /// The fill behind a photograph that has not arrived, and behind one that never
  /// will. Distinct from `neutral100` in the handoff by a hair, and the hair is
  /// deliberate: a missing image should read as an empty frame, not as a chip.
  static const Color imagePlaceholder = Color(0xFFEEF0EF);

  static const Color accent100 = Color(0xFFF0FDF4);
  static const Color accent200 = Color(0xFFDCFCE7);
  static const Color accent300 = Color(0xFFBBF7D0);
  static const Color accent400 = Color(0xFF86EFAC);
  static const Color accent500 = Color(0xFF22C55E);
  static const Color accent600 = Color(0xFF16A34A);
  static const Color accent700 = Color(0xFF15803D);
  static const Color accent900 = Color(0xFF14532D);

  /// Semantic status colours. Each carries its meaning against white, which is the
  /// only background this app has.
  static const Color ok = Color(0xFF15803D);
  static const Color warn = Color(0xFFB45309);
  static const Color bad = Color(0xFFDC2626);
  static const Color badStrong = Color(0xFF991B1B);

  /// The border and the fill the design gives a REJECTED document — the one row
  /// on a screen that has to be findable without reading it.
  static const Color badBorder = Color(0xFFFECACA);
  static const Color badTint = Color(0xFFFEE2E2);

  /// A rating star. Amber rather than the brand green, so a score never reads as
  /// an action or as approval by the platform.
  static const Color star = Color(0xFFF59E0B);

  static const Color divider = Color(0xFFECEEEC);
  static const Color dim = Color(0x9E111827);
}

/// Spacing, ported from the console's `--space-*` and read at phone scale.
abstract final class Space {
  static const double xs = 4;
  static const double sm = 8;
  static const double md = 12;
  static const double lg = 16;
  static const double xl = 24;
  static const double xxl = 32;

  /// The padding INSIDE a content card, which the design sets a step below the
  /// page gutter so a card does not read as a second, narrower page.
  static const double card = 14;

  /// Enough room under a scrolling page that the last row clears a bottom bar or
  /// a floating action, on the phones that put a gesture handle there too.
  static const double bottomInset = 96;
}

/// The handoff's radius system, named by ROLE rather than by size.
///
/// The design really does use different radii for different things — 7–8 on a
/// badge, 12 on an input, 13 on a button, 14 on a list row, 16 on a card, 18 on
/// the one card a screen leads with — and collapsing them to one value flattens a
/// distinction a reader can feel. Naming them by size instead (`sm`, `md`, `lg`)
/// just moves the problem: the call site then has to remember which size a
/// document row is, and it drifts the first time somebody guesses.
///
/// So the raw steps are private to this file and every call site asks for a shape.
abstract final class Radii {
  static const Radius _s8 = Radius.circular(8);
  static const Radius _s10 = Radius.circular(10);
  static const Radius _s12 = Radius.circular(12);
  static const Radius _s13 = Radius.circular(13);
  static const Radius _s14 = Radius.circular(14);
  static const Radius _s16 = Radius.circular(16);
  static const Radius _s18 = Radius.circular(18);

  /// A status badge, a spec chip, a thumbnail small enough to sit in a row.
  static const BorderRadius pill = BorderRadius.all(_s8);

  /// An input, and the image inside a list row.
  static const BorderRadius field = BorderRadius.all(_s12);

  /// A button. The design's own thirteen — between its fields and its rows,
  /// which is what makes a button read as raised against the form around it.
  static const BorderRadius button = BorderRadius.all(_s13);

  /// A row in a list: a document, a vehicle on a gallery's page.
  static const BorderRadius row = BorderRadius.all(_s14);

  /// The standard card, and the top of a bottom sheet.
  static const BorderRadius card = BorderRadius.all(_s16);

  /// The one card a screen leads with, where the design goes a step softer.
  static const BorderRadius feature = BorderRadius.all(_s18);

  /// A choice chip in a filter or a quick-pick row.
  ///
  /// NOT a lozenge. There is not one fully rounded shape anywhere in the handoff
  /// -- the softest corner it draws is the 22 on a sheet -- so a pill chip is the
  /// one control that would announce it came from somewhere else.
  static const BorderRadius chip = BorderRadius.all(_s10);

  /// The top corners only, for a sheet that rises from the bottom edge. The
  /// design goes softer here than anywhere else — a sheet is a different surface
  /// arriving, not a card that grew.
  static const BorderRadius sheetTop =
      BorderRadius.vertical(top: Radius.circular(22));

  /// The top corners of a card whose image runs to its edges.
  static const BorderRadius cardTop = BorderRadius.vertical(top: _s18);
}

/// The one shadow in the design: `0 1px 2px rgba(17,24,39,.04)`.
///
/// Barely visible on purpose. The handoff separates surfaces with a BORDER and
/// uses this only to lift a card a hair off the page behind it; anything heavier
/// would turn a flat, paper-like interface into a stack of floating panels.
abstract final class Shadows {
  static const List<BoxShadow> card = <BoxShadow>[
    BoxShadow(
      color: Color(0x0A111827),
      blurRadius: 2,
      offset: Offset(0, 1),
    ),
  ];
}

abstract final class KhadraTheme {
  /// The handoff's Latin face, bundled at 400–800.
  ///
  /// Named rather than left to the platform because the design is set in it and a
  /// system UI face is a different typeface on every phone the app runs on.
  static const String _font = 'Manrope';

  /// **Manrope contains no Arabic at all.** Not one letter, not the Arabic comma.
  /// Every Arabic glyph in this app is rendered by the first fallback that has it,
  /// and that is the whole Arabic typography strategy: the handoff itself declares
  /// `'Noto Kufi Arabic', Manrope` for Arabic, and this is the same decision
  /// expressed the way Flutter resolves fonts.
  ///
  /// Fallback is per GLYPH, not per string, which is what makes this work: a price
  /// or a booking reference inside an Arabic sentence keeps Manrope's figures while
  /// the words around it are set in Kufi. Ordering is therefore load-bearing —
  /// Noto Kufi Arabic carries Latin and digits too, so putting it first would
  /// silently re-set the entire English interface in it.
  ///
  /// The platform faces after it are not decoration. A bundled font can fail to
  /// load, and an Arabic interface falling through to a serif Naskh is survivable
  /// where falling through to nothing is not.
  static const List<String> _fontFallback = <String>[
    'Noto Kufi Arabic',
    'SF Arabic',
    'Geeza Pro',
    'Segoe UI',
    'Noto Sans Arabic',
    'Noto Naskh Arabic',
    'Tahoma',
  ];

  /// A style in the app's own faces. Every explicit style below goes through it,
  /// so none of them can quietly drop the Arabic fallback.
  static TextStyle _style({
    double? fontSize,
    FontWeight? fontWeight,
    Color? color,
    double? height,
    double? letterSpacing,
  }) =>
      TextStyle(
        fontFamily: _font,
        fontFamilyFallback: _fontFallback,
        fontSize: fontSize,
        fontWeight: fontWeight,
        color: color,
        height: height,
        letterSpacing: letterSpacing,
      );

  static ThemeData light() {
    const scheme = ColorScheme.light(
      primary: KhadraColors.accent,
      onPrimary: Colors.white,
      primaryContainer: KhadraColors.accent100,
      onPrimaryContainer: KhadraColors.accent900,
      secondary: KhadraColors.accentBright,
      onSecondary: Colors.white,
      surface: KhadraColors.surface,
      onSurface: KhadraColors.text,
      surfaceContainerHighest: KhadraColors.neutral100,
      error: KhadraColors.bad,
      onError: Colors.white,
      outline: KhadraColors.neutral300,
      outlineVariant: KhadraColors.neutral200,
    );

    final base = ThemeData(
      useMaterial3: true,
      colorScheme: scheme,
      scaffoldBackgroundColor: KhadraColors.background,
      fontFamily: _font,
      fontFamilyFallback: _fontFallback,
    );

    return base.copyWith(
      textTheme: _textTheme(base.textTheme),
      primaryTextTheme: _textTheme(base.primaryTextTheme),
      appBarTheme: AppBarTheme(
        backgroundColor: KhadraColors.surface,
        foregroundColor: KhadraColors.text,
        surfaceTintColor: Colors.transparent,
        elevation: 0,
        scrolledUnderElevation: 0.5,
        centerTitle: false,
        iconTheme: const IconThemeData(color: KhadraColors.text, size: 22),
        // SIXTEEN, which is the design's size for a screen you can go back from —
        // and most screens in this app are. A tab's own root asks for
        // `KhadraLargeTitle` instead. 800 either way: Manrope at 800 is a different
        // voice from Manrope at 600, and it is what makes the design read as
        // confident rather than administrative.
        titleTextStyle: _style(
          color: KhadraColors.text,
          fontSize: 16,
          fontWeight: FontWeight.w800,
          letterSpacing: -0.2,
        ),
      ),
      cardTheme: CardThemeData(
        color: KhadraColors.surface,
        surfaceTintColor: Colors.transparent,
        elevation: 0,
        margin: EdgeInsets.zero,
        shape: const RoundedRectangleBorder(
          borderRadius: Radii.card,
          side: BorderSide(color: KhadraColors.neutral200),
        ),
      ),
      dividerTheme: const DividerThemeData(
        color: KhadraColors.neutral200,
        thickness: 1,
        space: 1,
      ),
      filledButtonTheme: FilledButtonThemeData(
        style: FilledButton.styleFrom(
          minimumSize: const Size.fromHeight(52),
          backgroundColor: KhadraColors.accent,
          foregroundColor: Colors.white,
          disabledBackgroundColor: KhadraColors.neutral200,
          disabledForegroundColor: KhadraColors.neutral500,
          shape: const RoundedRectangleBorder(borderRadius: Radii.button),
          textStyle: _style(fontSize: 15, fontWeight: FontWeight.w700),
        ),
      ),
      outlinedButtonTheme: OutlinedButtonThemeData(
        style: OutlinedButton.styleFrom(
          minimumSize: const Size.fromHeight(52),
          // The handoff's secondary button is green ON a green tint, not green on
          // white: a pale wash plus a pale border, which reads as an action without
          // competing with the filled one beside it.
          foregroundColor: KhadraColors.accent,
          backgroundColor: KhadraColors.accent100,
          side: const BorderSide(color: KhadraColors.accent300),
          shape: const RoundedRectangleBorder(borderRadius: Radii.button),
          textStyle: _style(fontSize: 15, fontWeight: FontWeight.w700),
        ),
      ),
      textButtonTheme: TextButtonThemeData(
        style: TextButton.styleFrom(
          foregroundColor: KhadraColors.accent,
          // An inline link, not a third button: the design sets these two steps
          // under the filled button they sit beside.
          textStyle: _style(fontSize: 13, fontWeight: FontWeight.w700),
        ),
      ),
      inputDecorationTheme: InputDecorationTheme(
        filled: true,
        fillColor: KhadraColors.surface,
        contentPadding: const EdgeInsets.symmetric(
          horizontal: Space.lg,
          vertical: Space.lg,
        ),
        border: const OutlineInputBorder(
          borderRadius: Radii.field,
          borderSide: BorderSide(color: KhadraColors.neutral300),
        ),
        enabledBorder: const OutlineInputBorder(
          borderRadius: Radii.field,
          borderSide: BorderSide(color: KhadraColors.neutral300),
        ),
        focusedBorder: const OutlineInputBorder(
          borderRadius: Radii.field,
          borderSide: BorderSide(color: KhadraColors.accent, width: 2),
        ),
        errorBorder: const OutlineInputBorder(
          borderRadius: Radii.field,
          borderSide: BorderSide(color: KhadraColors.bad),
        ),
        focusedErrorBorder: const OutlineInputBorder(
          borderRadius: Radii.field,
          borderSide: BorderSide(color: KhadraColors.bad, width: 2),
        ),
        labelStyle: _style(color: KhadraColors.neutral600, fontWeight: FontWeight.w600),
        hintStyle: _style(color: KhadraColors.neutral500, fontWeight: FontWeight.w500),
      ),
      chipTheme: ChipThemeData(
        backgroundColor: KhadraColors.surface,
        selectedColor: KhadraColors.accent100,
        // A CHOSEN chip is tinted and outlined in the accent, not filled with it.
        // A solid green lozenge in a row of white ones reads as a button somebody
        // has not pressed yet; the tint reads as a choice already made.
        side: WidgetStateBorderSide.resolveWith(
          (states) => BorderSide(
            color: states.contains(WidgetState.selected)
                ? KhadraColors.accent
                : KhadraColors.neutral300,
          ),
        ),
        shape: const RoundedRectangleBorder(borderRadius: Radii.chip),
        labelStyle: WidgetStateTextStyle.resolveWith(
          (states) => _style(
            fontSize: 12,
            fontWeight: states.contains(WidgetState.selected)
                ? FontWeight.w700
                : FontWeight.w600,
            color: states.contains(WidgetState.selected)
                ? KhadraColors.price
                : KhadraColors.neutral800,
          ),
        ),
        showCheckmark: false,
        padding: const EdgeInsets.symmetric(horizontal: 13, vertical: 9),
      ),
      bottomNavigationBarTheme: const BottomNavigationBarThemeData(
        backgroundColor: KhadraColors.surface,
        selectedItemColor: KhadraColors.accent,
        unselectedItemColor: KhadraColors.neutral500,
        type: BottomNavigationBarType.fixed,
        elevation: 0,
      ),
      navigationBarTheme: NavigationBarThemeData(
        backgroundColor: KhadraColors.surface,
        surfaceTintColor: Colors.transparent,
        // NO indicator pill. The design marks the active tab by colouring the icon
        // and its label green, and a filled lozenge behind one of five icons reads
        // as a sixth control rather than as a state.
        indicatorColor: Colors.transparent,
        indicatorShape: const RoundedRectangleBorder(borderRadius: Radii.chip),
        elevation: 0,
        height: 64,
        labelTextStyle: WidgetStateProperty.resolveWith(
          (states) => _style(
            fontSize: 10,
            fontWeight: FontWeight.w700,
            color: states.contains(WidgetState.selected)
                ? KhadraColors.accent
                : KhadraColors.neutral600,
          ),
        ),
        iconTheme: WidgetStateProperty.resolveWith(
          (states) => IconThemeData(
            size: 22,
            color: states.contains(WidgetState.selected)
                ? KhadraColors.accent
                : KhadraColors.neutral600,
          ),
        ),
      ),
      snackBarTheme: SnackBarThemeData(
        behavior: SnackBarBehavior.floating,
        backgroundColor: KhadraColors.neutral900,
        contentTextStyle: _style(color: Colors.white, fontWeight: FontWeight.w600),
        shape: const RoundedRectangleBorder(borderRadius: Radii.field),
      ),
      dialogTheme: const DialogThemeData(
        backgroundColor: KhadraColors.surface,
        surfaceTintColor: Colors.transparent,
        shape: RoundedRectangleBorder(borderRadius: Radii.card),
      ),
      bottomSheetTheme: const BottomSheetThemeData(
        backgroundColor: KhadraColors.surface,
        surfaceTintColor: Colors.transparent,
        // The design's scrim: near-black at 45%, so the page behind is still
        // legible as context rather than blacked out.
        modalBarrierColor: Color(0x73111827),
        showDragHandle: true,
        dragHandleColor: KhadraColors.neutral300,
        dragHandleSize: Size(38, 4),
        shape: RoundedRectangleBorder(
          borderRadius: Radii.sheetTop,
        ),
      ),
      progressIndicatorTheme: const ProgressIndicatorThemeData(
        color: KhadraColors.accent,
      ),
      listTileTheme: const ListTileThemeData(
        iconColor: KhadraColors.neutral600,
        textColor: KhadraColors.text,
      ),
    );
  }

  /// The handoff's hierarchy, as the scale every screen inherits from.
  ///
  /// Heavier at the top than Material's defaults and tighter at the bottom: the
  /// design leans on weight rather than size to separate a title from the line
  /// under it, which is what keeps a card readable at 375px.
  static TextTheme _textTheme(TextTheme base) => base
      .apply(
        bodyColor: KhadraColors.text,
        displayColor: KhadraColors.text,
        fontFamily: _font,
        fontFamilyFallback: _fontFallback,
      )
      .copyWith(
        headlineSmall: _style(
            fontSize: 20, fontWeight: FontWeight.w800, letterSpacing: -0.4,
            color: KhadraColors.text),
        titleLarge: _style(
            fontSize: 17, fontWeight: FontWeight.w800, letterSpacing: -0.2,
            color: KhadraColors.text),
        titleMedium: _style(
            fontSize: 15, fontWeight: FontWeight.w700, color: KhadraColors.text),
        titleSmall: _style(
            fontSize: 13, fontWeight: FontWeight.w700, color: KhadraColors.text),
        bodyLarge: _style(
            fontSize: 15, fontWeight: FontWeight.w500, height: 1.5,
            color: KhadraColors.text),
        bodyMedium: _style(
            fontSize: 14, fontWeight: FontWeight.w500, height: 1.5,
            color: KhadraColors.text),
        bodySmall: _style(
            fontSize: 12, fontWeight: FontWeight.w600,
            color: KhadraColors.neutral600),
        labelLarge: _style(fontSize: 15, fontWeight: FontWeight.w700),
        labelMedium: _style(
            fontSize: 12, fontWeight: FontWeight.w600,
            color: KhadraColors.neutral600),
        labelSmall: _style(
            fontSize: 11, fontWeight: FontWeight.w600,
            color: KhadraColors.neutral500),
      );
}
