import 'package:flutter/material.dart';
import 'package:flutter/semantics.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:package_info_plus/package_info_plus.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:timezone/data/latest_all.dart' as tz_data;

import 'core/config/app_environment.dart';
import 'core/config/update_requirement.dart';
import 'core/providers.dart';
import 'core/router.dart';
import 'core/theme/khadra_theme.dart';
import 'core/widgets/environment_ribbon.dart';
import 'features/update/update_required_screen.dart';
import 'l10n/app_localizations.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();

  // Loaded before the first frame because every calendar answer on this platform
  // is in Amman and a date picker that opened before the zone database was ready
  // would run in the device's zone -- and price a different number of days.
  tz_data.initializeTimeZones();

  // A staging build given a production address, or a production build given the
  // staging one, stops HERE with a sentence naming the mistake -- before a single
  // request is made to the wrong server.
  AppEnvironment.verify();

  // On the web Flutter builds no accessibility tree until something asks for one:
  // it renders into a canvas, and the tree is expensive, so the engine waits for a
  // screen reader to announce itself. That is the right default for a customer and
  // the wrong one for a machine driving the app, which otherwise sees an empty DOM.
  //
  // A dart-define rather than a debug check, because turning it on must be a
  // deliberate act:
  //
  //     flutter run -d web-server --dart-define=KHADRA_FORCE_SEMANTICS=true
  //
  // Nothing about the app's behaviour changes; it only stops waiting to be asked.
  if (const bool.fromEnvironment('KHADRA_FORCE_SEMANTICS')) {
    SemanticsBinding.instance.ensureSemantics();
  }

  final preferences = await SharedPreferences.getInstance();
  final installedVersion = await _installedVersion();

  runApp(
    ProviderScope(
      overrides: [
        sharedPreferencesProvider.overrideWithValue(preferences),
        installedAppVersionProvider.overrideWithValue(installedVersion),
      ],
      child: const KhadraApp(),
    ),
  );
}

/// This build's version as the platform reports it — `1.1.0+2` — or null.
///
/// It goes on every request as `X-Khadra-App-Version`, and it is what the update
/// screen compares with the minimum the API publishes. Read from the platform
/// rather than typed into a constant, because a constant is a second copy of the
/// pubspec's `version:` that nobody remembers to bump.
///
/// A failure here must not take the app down, and must not be papered over with a
/// made-up version either: null sends no header, and the API refuses this phone
/// like any build that predates the header — the update screen goes up, which is
/// the honest answer for a build nobody can identify.
Future<String?> _installedVersion() async {
  try {
    final info = await PackageInfo.fromPlatform();
    final version = info.version.trim();
    if (version.isEmpty) return null;
    final build = info.buildNumber.trim();
    return build.isEmpty ? version : '$version+$build';
  } on Object {
    return null;
  }
}

class KhadraApp extends ConsumerStatefulWidget {
  const KhadraApp({super.key});

  @override
  ConsumerState<KhadraApp> createState() => _KhadraAppState();
}

class _KhadraAppState extends ConsumerState<KhadraApp> {
  /// Set the first time an update is required, and never cleared for the life of
  /// the process. Swapping the router out disposes every screen under it, and a
  /// requirement that could flip back would put them all up again, half-loaded,
  /// on a build the server has already refused.
  UpdateRequirement? _blockedBy;

  @override
  void initState() {
    super.initState();
    // One cold-start rotation, before the first screen decides what to show. Until
    // it answers the session is `unknown`, which is why that state exists: folding
    // it into "signed out" would flash the sign-in screen at somebody who is
    // signed in, on every launch.
    WidgetsBinding.instance.addPostFrameCallback((_) {
      ref.read(sessionProvider.notifier).restore();
    });
  }

  @override
  Widget build(BuildContext context) {
    final router = ref.watch(routerProvider);
    final locale = ref.watch(localeProvider);
    final blockedBy = _blockedBy ??= ref.watch(updateRequirementProvider);

    return MaterialApp.router(
      // The name Android shows in the task switcher and the web tab, in the
      // reader's own language. `title:` is a constant evaluated before there is a
      // locale to ask, so an Arabic customer found "Khadra" among their open apps
      // while every other word in it was Arabic. `onGenerateTitle` runs with a
      // context that has the localisations in it.
      onGenerateTitle: (context) => AppLocalizations.of(context).appName,
      debugShowCheckedModeBanner: false,
      // The language decides the type scale as well as the words: Arabic is a
      // joined script and the design's letter spacing is drawn for Latin.
      theme: KhadraTheme.light(arabic: ref.watch(isArabicProvider)),
      routerConfig: router,

      // Null follows the device, which is the right default on a first run: a
      // phone set to Arabic should open in Arabic without being asked.
      locale: locale,

      // What "follows the device" means for a device asking for NEITHER language.
      //
      // Left to Flutter this is `supportedLocales.first`, and that list is
      // generated from the ARB file names — so a phone set to Turkish, French or
      // Russian opened this app in Arabic, right to left, purely because `ar`
      // sorts before `en`. The same function answers `isArabicProvider` and
      // `formatsProvider`, so the interface and the data it renders can no longer
      // disagree about which language this is.
      localeListResolutionCallback: (deviceLocales, supported) =>
          resolveKhadraLocale(locale, deviceLocales ?? const <Locale>[]),
      supportedLocales: AppLocalizations.supportedLocales,
      localizationsDelegates: const [
        AppLocalizations.delegate,
        GlobalMaterialLocalizations.delegate,
        GlobalWidgetsLocalizations.delegate,
        GlobalCupertinoLocalizations.delegate,
      ],

      builder: (context, child) {
        // Text scaling is honoured up to a point. Past 1.4 the booking summary's
        // figures start colliding with their labels, and a customer who cannot
        // read a price is worse off than one reading a slightly smaller one.
        final media = MediaQuery.of(context);
        return MediaQuery(
          data: media.copyWith(
            textScaler: media.textScaler.clamp(minScaleFactor: 0.9, maxScaleFactor: 1.4),
          ),
          // While this build is too old to use, the update screen stands in for
          // the router itself — not a route on it — so no route and no deep link
          // can reach past it: the router is not in the tree to receive one. Its
          // own Navigator is for the language menu, whose popup needs an overlay.
          //
          // The staging build's ribbon wraps BOTH, so even the update screen says
          // which server refused it. In production it returns the child untouched.
          child: EnvironmentRibbon(
            child: blockedBy != null
                ? Navigator(
                    onGenerateRoute: (_) => MaterialPageRoute<void>(
                      builder: (_) => UpdateRequiredScreen(requirement: blockedBy),
                    ),
                  )
                : child ?? const SizedBox.shrink(),
          ),
        );
      },
    );
  }
}
