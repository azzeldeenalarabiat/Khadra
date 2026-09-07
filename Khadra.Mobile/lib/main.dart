import 'package:flutter/material.dart';
import 'package:flutter/semantics.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:timezone/data/latest_all.dart' as tz_data;

import 'core/providers.dart';
import 'core/router.dart';
import 'core/theme/khadra_theme.dart';
import 'l10n/app_localizations.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();

  // Loaded before the first frame because every calendar answer on this platform
  // is in Amman and a date picker that opened before the zone database was ready
  // would run in the device's zone -- and price a different number of days.
  tz_data.initializeTimeZones();

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

  runApp(
    ProviderScope(
      overrides: [sharedPreferencesProvider.overrideWithValue(preferences)],
      child: const KhadraApp(),
    ),
  );
}

class KhadraApp extends ConsumerStatefulWidget {
  const KhadraApp({super.key});

  @override
  ConsumerState<KhadraApp> createState() => _KhadraAppState();
}

class _KhadraAppState extends ConsumerState<KhadraApp> {
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

    return MaterialApp.router(
      title: 'Khadra',
      debugShowCheckedModeBanner: false,
      theme: KhadraTheme.light(),
      routerConfig: router,

      // Null follows the device, which is the right default on a first run: a
      // phone set to Arabic should open in Arabic without being asked.
      locale: locale,
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
          child: child ?? const SizedBox.shrink(),
        );
      },
    );
  }
}
