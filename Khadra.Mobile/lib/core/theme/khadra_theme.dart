import 'package:flutter/material.dart';

/// The Khadra palette, ported value-for-value from
/// `Khadra.Dashboard/src/styles/_tokens.scss`.
///
/// Ported rather than re-picked. The console, the dealer console and this app are
/// three faces of one platform, and a green chosen by eye on a phone would be a
/// fourth brand nobody decided on. `_tokens.scss` says of itself that it is the
/// source of truth for the look; this file is that file's shadow, and any change
/// to the brand starts there.
///
/// The GEOMETRY is ported too — the same radii, the same 2.8px spacing step — and
/// then read at phone sizes: the console's step is small because it packs dense
/// tables, so the app uses multiples of it rather than the step itself.
abstract final class KhadraColors {
  static const Color background = Color(0xFFF6F7F7);
  static const Color surface = Color(0xFFFFFFFF);
  static const Color text = Color(0xFF14181C);

  /// The logo's green. Everything that carries the brand uses this one.
  static const Color accent = Color(0xFF146C34);
  static const Color accentBright = Color(0xFF16A34A);

  static const Color neutral100 = Color(0xFFF5F6F6);
  static const Color neutral200 = Color(0xFFE6E8E8);
  static const Color neutral300 = Color(0xFFD2D5D6);
  static const Color neutral400 = Color(0xFFB0B5B7);
  static const Color neutral500 = Color(0xFF8B9194);
  static const Color neutral600 = Color(0xFF6B7174);
  static const Color neutral700 = Color(0xFF4D5356);
  static const Color neutral800 = Color(0xFF32383B);
  static const Color neutral900 = Color(0xFF1C2124);

  static const Color accent100 = Color(0xFFEEF8F1);
  static const Color accent200 = Color(0xFFD6EFDF);
  static const Color accent300 = Color(0xFFAADFBF);
  static const Color accent400 = Color(0xFF6EC894);
  static const Color accent500 = Color(0xFF35AB6C);
  static const Color accent600 = Color(0xFF1F8B52);
  static const Color accent700 = Color(0xFF146C34);
  static const Color accent900 = Color(0xFF0B3F1F);

  /// Semantic status colours, darkened from pastels so they carry their meaning
  /// against white — the same reasoning `_tokens.scss` gives.
  static const Color ok = Color(0xFF146C34);
  static const Color warn = Color(0xFFB45309);
  static const Color bad = Color(0xFFB42318);
  static const Color badStrong = Color(0xFFD32F2F);

  static const Color divider = Color(0x2414181C);
  static const Color dim = Color(0x9E14181C);
}

/// Spacing, ported from the console's `--space-*` and read at phone scale.
abstract final class Space {
  static const double xs = 4;
  static const double sm = 8;
  static const double md = 12;
  static const double lg = 16;
  static const double xl = 24;
  static const double xxl = 32;

  /// Enough room under a scrolling page that the last row clears a bottom bar or
  /// a floating action, on the phones that put a gesture handle there too.
  static const double bottomInset = 96;
}

abstract final class Radii {
  static const Radius sm = Radius.circular(6);
  static const Radius md = Radius.circular(10);
  static const Radius lg = Radius.circular(16);

  static const BorderRadius card = BorderRadius.all(lg);
  static const BorderRadius field = BorderRadius.all(md);
  static const BorderRadius chip = BorderRadius.all(Radius.circular(999));
}

abstract final class KhadraTheme {
  /// Arabic faces are APPENDED, not substituted — the console's decision, and the
  /// reason is that font fallback is per GLYPH: Latin keeps the platform's UI face
  /// while Arabic, which that face may not cover at all, picks the first fallback
  /// that does. Without naming them, Arabic lands on whatever the platform has
  /// last, which is usually a serif Naskh that does not sit level with the Latin
  /// beside it.
  static const List<String> _fontFallback = <String>[
    'SF Arabic',
    'Geeza Pro',
    'Segoe UI',
    'Noto Sans Arabic',
    'Noto Naskh Arabic',
    'Tahoma',
  ];

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
    );

    return base.copyWith(
      textTheme: _textTheme(base.textTheme),
      primaryTextTheme: _textTheme(base.primaryTextTheme),
      appBarTheme: const AppBarTheme(
        backgroundColor: KhadraColors.surface,
        foregroundColor: KhadraColors.text,
        surfaceTintColor: Colors.transparent,
        elevation: 0,
        scrolledUnderElevation: 0.5,
        centerTitle: false,
        titleTextStyle: TextStyle(
          color: KhadraColors.text,
          fontSize: 18,
          fontWeight: FontWeight.w600,
          fontFamilyFallback: _fontFallback,
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
          shape: const RoundedRectangleBorder(borderRadius: Radii.field),
          textStyle: const TextStyle(
            fontSize: 16,
            fontWeight: FontWeight.w600,
            fontFamilyFallback: _fontFallback,
          ),
        ),
      ),
      outlinedButtonTheme: OutlinedButtonThemeData(
        style: OutlinedButton.styleFrom(
          minimumSize: const Size.fromHeight(52),
          foregroundColor: KhadraColors.accent,
          side: const BorderSide(color: KhadraColors.accent300),
          shape: const RoundedRectangleBorder(borderRadius: Radii.field),
          textStyle: const TextStyle(
            fontSize: 16,
            fontWeight: FontWeight.w600,
            fontFamilyFallback: _fontFallback,
          ),
        ),
      ),
      textButtonTheme: TextButtonThemeData(
        style: TextButton.styleFrom(
          foregroundColor: KhadraColors.accent,
          textStyle: const TextStyle(
            fontSize: 15,
            fontWeight: FontWeight.w600,
            fontFamilyFallback: _fontFallback,
          ),
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
        labelStyle: const TextStyle(
          color: KhadraColors.neutral600,
          fontFamilyFallback: _fontFallback,
        ),
        hintStyle: const TextStyle(
          color: KhadraColors.neutral500,
          fontFamilyFallback: _fontFallback,
        ),
      ),
      chipTheme: ChipThemeData(
        backgroundColor: KhadraColors.surface,
        selectedColor: KhadraColors.accent100,
        side: const BorderSide(color: KhadraColors.neutral300),
        shape: const RoundedRectangleBorder(borderRadius: Radii.chip),
        labelStyle: const TextStyle(
          fontSize: 14,
          color: KhadraColors.text,
          fontFamilyFallback: _fontFallback,
        ),
        padding: const EdgeInsets.symmetric(horizontal: Space.md, vertical: Space.sm),
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
        indicatorColor: KhadraColors.accent100,
        elevation: 0,
        height: 68,
        labelTextStyle: WidgetStateProperty.resolveWith(
          (states) => TextStyle(
            fontSize: 12,
            fontWeight: states.contains(WidgetState.selected)
                ? FontWeight.w600
                : FontWeight.w500,
            color: states.contains(WidgetState.selected)
                ? KhadraColors.accent
                : KhadraColors.neutral600,
            fontFamilyFallback: _fontFallback,
          ),
        ),
      ),
      snackBarTheme: const SnackBarThemeData(
        behavior: SnackBarBehavior.floating,
        backgroundColor: KhadraColors.neutral900,
        contentTextStyle: TextStyle(
          color: Colors.white,
          fontFamilyFallback: _fontFallback,
        ),
        shape: RoundedRectangleBorder(borderRadius: Radii.field),
      ),
      dialogTheme: const DialogThemeData(
        backgroundColor: KhadraColors.surface,
        surfaceTintColor: Colors.transparent,
        shape: RoundedRectangleBorder(borderRadius: Radii.card),
      ),
      bottomSheetTheme: const BottomSheetThemeData(
        backgroundColor: KhadraColors.surface,
        surfaceTintColor: Colors.transparent,
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.vertical(top: Radii.lg),
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

  static TextTheme _textTheme(TextTheme base) => base.apply(
        bodyColor: KhadraColors.text,
        displayColor: KhadraColors.text,
        fontFamilyFallback: _fontFallback,
      );
}
