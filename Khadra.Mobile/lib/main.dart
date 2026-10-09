import 'package:flutter/material.dart';
import 'package:flutter/semantics.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:package_info_plus/package_info_plus.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:timezone/data/latest_all.dart' as tz_data;

import 'core/config/app_environment.dart';
import 'core/config/update_requirement.dart';
import 'core/fonts/font_licences.dart';
import 'core/live/live_refresh.dart';
import 'core/providers.dart';
import 'core/push/push_actions.dart';
import 'core/router.dart';
import 'core/session/session_controller.dart';
import 'core/theme/khadra_theme.dart';
import 'features/legal/consent_prompt_screen.dart';
import 'features/update/update_required_screen.dart';
import 'l10n/app_localizations.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();

  // The fonts' licence texts join the packages' in Flutter's licence registry.
  registerFontLicences();

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

class _KhadraAppState extends ConsumerState<KhadraApp> with WidgetsBindingObserver {
  /// Set the first time an update is required, and never cleared for the life of
  /// the process. Swapping the router out disposes every screen under it, and a
  /// requirement that could flip back would put them all up again, half-loaded,
  /// on a build the server has already refused.
  UpdateRequirement? _blockedBy;

  /// The consent prompt's own Navigator, while it stands in for the router.
  final _consentNavigator = GlobalKey<NavigatorState>(debugLabel: 'consent-prompt');

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    // One cold-start rotation, before the first screen decides what to show. Until
    // it answers the session is `unknown`, which is why that state exists: folding
    // it into "signed out" would flash the sign-in screen at somebody who is
    // signed in, on every launch.
    WidgetsBinding.instance.addPostFrameCallback((_) {
      ref.read(sessionProvider.notifier).restore();
      _startPush();
    });
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    super.dispose();
  }

  /// Android Back while the consent prompt stands in for the router: a text opened from it
  /// closes, and the prompt is back. Nothing else answers it there — the router's back button
  /// dispatcher went with the router — so without this Back closed the app from a text the
  /// person was reading in order to accept. On the prompt itself it closes the app, as before.
  /// Whenever the router is in the tree the key resolves to nothing and this declines.
  @override
  Future<bool> didPopRoute() async => await _consentNavigator.currentState?.maybePop() ?? false;

  /// Raises the consent prompt when `/auth/me` names a text still to accept, and drops it when
  /// nobody is signed in (pre-launch item 238).
  void _syncConsent(SessionState session) {
    final pending = session.user?.pendingConsents ?? const [];
    if (!session.isSignedIn) {
      ref.read(consentRequiredProvider.notifier).state = false;
    } else if (pending.isNotEmpty) {
      ref.read(consentRequiredProvider.notifier).state = true;
    }
    _holdLiveSurfaces();
  }

  /// Every live surface holds while the prompt is up: behind it each of their reads would only
  /// be refused. Never raises the prompt — only the session and the interceptor do.
  void _holdLiveSurfaces() => ref
      .read(liveRefreshProvider)
      .setHeld(ref.read(sessionProvider).isSignedIn && ref.read(consentRequiredProvider));

  /// Nothing is pending any more: the app comes back where the router left it, the phone is
  /// registered for push (refused while consent was pending, deliberately: no push before the
  /// privacy notice is accepted), and the account is re-read.
  ///
  /// The acceptance's own answer is the authority, applied to the account FIRST. Waiting for
  /// `/auth/me` instead let a re-read that failed leave the old list in place, and the prompt
  /// went straight back up over texts already accepted. A text published since is still asked
  /// for: the re-read below names it, and the session raises the prompt again.
  Future<void> _consentAccepted() async {
    final session = ref.read(sessionProvider.notifier);
    final user = ref.read(sessionProvider).user;
    if (user != null) session.applyUser(user.withPendingConsents(const []));
    ref.read(consentRequiredProvider.notifier).state = false;
    await session.reload();
    await ref.read(pushCoordinatorProvider).signedIn(ref.read(appLanguageProvider));
  }

  /// Push notifications, for the life of the app. Off, silently, when this build has no
  /// Firebase project behind it.
  void _startPush() {
    final push = ref.read(pushCoordinatorProvider)
      // A tap opens what it is about (notificationRoute), through the router's own guards,
      // and survives a cold start (openForPush).
      ..navigate = (location) {
        openForPush(
          location,
          router: ref.read(routerProvider),
          sessionResolved: ref.read(sessionProvider).isResolved,
        );
      }
      // A push in front refreshes what it might be showing, now rather than at the next
      // poll: its booking, My Documents, and every live surface (refreshForPush).
      ..refresh = (data) => refreshForPush(
            data,
            invalidate: ref.invalidate,
            live: ref.read(liveRefreshProvider),
          );
    push.start();
  }

  @override
  Widget build(BuildContext context) {
    // The phone is registered for push while, and only while, somebody is signed in, and
    // re-registered in the language the app is showing. See PushCoordinator.
    ref.listen(sessionProvider, (previous, next) {
      final push = ref.read(pushCoordinatorProvider);
      if (next.isSignedIn && previous?.isSignedIn != true) {
        push.signedIn(ref.read(appLanguageProvider));
        // The sign-in and refresh answers do not say whether a legal text waits for
        // this person; `/auth/me` does (pre-launch item 238). One read per session.
        ref.read(sessionProvider.notifier).reload();
      } else if (!next.isSignedIn && previous?.isSignedIn == true) {
        push.sessionEnded();
      }
      _syncConsent(next);
    });
    ref.listen(consentRequiredProvider, (_, __) => _holdLiveSurfaces());
    ref.listen(appLanguageProvider, (_, language) {
      ref.read(pushCoordinatorProvider).languageChanged(language);
    });

    final router = ref.watch(routerProvider);
    final locale = ref.watch(localeProvider);
    final blockedBy = _blockedBy ??= ref.watch(updateRequirementProvider);
    // The consent prompt stands in for the app while a signed-in person has a text to
    // accept. An update requirement outranks it: a build the server no longer serves
    // cannot be asked anything, and the new build asks again.
    final consenting = blockedBy == null &&
        ref.watch(consentRequiredProvider) &&
        ref.watch(sessionProvider.select((session) => session.isSignedIn));

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
          child: blockedBy != null
              ? Navigator(
                  onGenerateRoute: (_) => MaterialPageRoute<void>(
                    builder: (_) => UpdateRequiredScreen(requirement: blockedBy),
                  ),
                )
              // The same way, for the same reason: no tab, route or deep link behind it
              // (pre-launch item 238). Its own Navigator also carries the texts it opens.
              : consenting
                  ? Navigator(
                      key: _consentNavigator,
                      onGenerateRoute: (_) => MaterialPageRoute<void>(
                        builder: (_) => ConsentPromptScreen(onAccepted: _consentAccepted),
                      ),
                    )
                  : child ?? const SizedBox.shrink(),
        );
      },
    );
  }
}
