import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:timezone/timezone.dart' as tz;

import '../api/dtos.dart';
import '../api/khadra_api.dart';
import 'api/api_client.dart';
import 'api/auth_interceptor.dart';
import 'format/formats.dart';
import 'session/session_controller.dart';
import 'session/session_store.dart';

/// The composition root.
///
/// One rule runs through it: **no provider holds another screen's data.** Each
/// screen watches the providers it needs and nothing else, which is the app's
/// version of the standing rule that a screen calls the APIs it needs and only
/// those. A "current everything" provider would quietly make every screen depend
/// on every fetch.

/// The token store, holding the preferences it needs to disown a session.
///
/// The marker that says "these tokens are ours" lives in ordinary preferences
/// rather than in the secure store, because it has to be readable when the secure
/// store is not. See [SessionStore.sessionIsOwned].
final sessionStoreProvider = Provider<SessionStore>(
  (ref) => SessionStore(preferences: ref.watch(sharedPreferencesProvider)),
);

/// The Dio instance, wired to the session before anything can use it.
///
/// The interceptor is installed here rather than inside `ApiClient` because it
/// needs the session controller, which needs the API — a cycle that is broken by
/// reading the controller lazily through `ref` at call time.
// Explicitly annotated, all three of them: the client needs the session to
// refresh, the session needs the API, and the API needs the client. That is a real
// cycle at the type level and Dart cannot infer through it -- it is broken at
// RUNTIME by reading the session lazily inside the interceptor callbacks, which is
// the same trick, one layer down.
final Provider<ApiClient> apiClientProvider = Provider<ApiClient>((ref) {
  final dio = ApiClient.createDio();
  final store = ref.watch(sessionStoreProvider);

  dio.interceptors.add(AuthInterceptor(
    store: store,
    refresh: () => ref.read(sessionProvider.notifier).refresh(),
    onSessionEnded: () => ref.read(sessionProvider.notifier).endSession(),
  ));

  return ApiClient(dio);
});

final Provider<KhadraApi> apiProvider =
    Provider<KhadraApi>((ref) => KhadraApi(ref.watch(apiClientProvider)));

final StateNotifierProvider<SessionController, SessionState> sessionProvider =
    StateNotifierProvider<SessionController, SessionState>((ref) {
  return SessionController(
    api: ref.watch(apiProvider),
    store: ref.watch(sessionStoreProvider),
  );
});

// ── Language ───────────────────────────────────────────────────────────────────

/// The chosen language, remembered on the device.
///
/// A per-device convenience rather than an account setting: this platform has no
/// `PreferredLanguage` on a user yet (pre-launch checklist item 40), so a customer
/// who signs in on a second phone starts in that phone's language. Recorded rather
/// than pretended otherwise.
class LocaleController extends StateNotifier<Locale?> {
  LocaleController(this._preferences) : super(_read(_preferences));

  static const _key = 'khadra.locale';

  final SharedPreferences? _preferences;

  static Locale? _read(SharedPreferences? preferences) {
    final code = preferences?.getString(_key);
    return code == null ? null : Locale(code);
  }

  /// Null means "follow the device", which is the right default for a first run:
  /// a phone set to Arabic should open in Arabic without being asked.
  Future<void> set(Locale? locale) async {
    state = locale;
    if (locale == null) {
      await _preferences?.remove(_key);
    } else {
      await _preferences?.setString(_key, locale.languageCode);
    }
  }
}

/// The language the app will actually be READ in.
///
/// One answer, used by the framework and by every provider that has to agree with
/// it. They did not agree, and the gap was invisible in both supported languages:
///
/// `MaterialApp` with no `locale` resolves through `basicLocaleListResolution`,
/// which falls back to `supportedLocales.first` when the device matches nothing —
/// and that list is generated from the ARB file names, so it is `[ar, en]` by
/// alphabet. A phone set to Turkish, French or Russian therefore got an ARABIC,
/// right-to-left interface. Meanwhile these providers looked at
/// `platformDispatcher.locale.languageCode`, saw `tr`, and handed that same screen
/// English city names, English car types and English dates.
///
/// So the fallback is stated rather than inherited from an alphabet: Arabic for a
/// device asking for Arabic, English for everything else. English is the right
/// default for the third language — this is a marketplace serving visitors to
/// Jordan, and an interface nobody can read is worse in Arabic than in English.
Locale resolveKhadraLocale(Locale? chosen, List<Locale> deviceLocales) {
  // A language the customer picked in Profile beats anything the device says.
  if (chosen != null) return chosen;

  for (final locale in deviceLocales) {
    if (locale.languageCode == 'ar') return const Locale('ar');
    if (locale.languageCode == 'en') return const Locale('en');
  }

  return const Locale('en');
}

final sharedPreferencesProvider = Provider<SharedPreferences?>(
  (ref) => throw UnimplementedError('Overridden in main() once loaded.'),
);

final localeProvider = StateNotifierProvider<LocaleController, Locale?>(
  (ref) => LocaleController(ref.watch(sharedPreferencesProvider)),
);

// ── How this device is being used ──────────────────────────────────────────────

/// Whether the person holding this device has SAID how they want to use the app.
///
/// True once they have chosen at the Get Started screen — browse as a guest, sign
/// in, or create an account. False on a fresh install, after the app's data has
/// been cleared, and after a deliberate sign-out.
///
/// **It is not a session state, and deliberately not a fourth [SessionStatus].**
/// Signed out is signed out whether or not a choice was made; the two answer
/// different questions, and folding this into the credential state machine would
/// put a stored preference in front of `restore()` and the router's refresh
/// listener, neither of which has any business with one.
///
/// **In ordinary preferences, not the secure store**, because "the app's data was
/// cleared" is exactly what has to bring the Get Started screen back — and because
/// iOS keeps Keychain items when an app is deleted, so a flag kept there would
/// make a reinstall skip it.
///
/// An expiry or a suspension does NOT clear it. Somebody whose session ran out has
/// an account and has long since made their choice; asking them to make it again
/// would be the app forgetting who it is talking to.
class EntryChoice extends StateNotifier<bool> {
  EntryChoice(this._preferences)
      : super(_preferences?.getBool(_key) ?? false);

  static const _key = 'khadra.entry_chosen';

  final SharedPreferences? _preferences;

  /// Remembers that a choice has been made, so the next launch opens where they
  /// left off rather than asking again.
  ///
  /// The state moves SYNCHRONOUSLY and the write follows, which is what lets a
  /// caller carry on without waiting: everything in this run reads the new answer
  /// immediately, and only the next launch depends on the write.
  Future<void> choose() => _remember(true);

  /// Forgets it, which returns the app to the unauthenticated flow. Called on a
  /// deliberate sign-out and nowhere else.
  Future<void> forget() => _remember(false);

  /// Neither of these may THROW.
  ///
  /// Both are called immediately after something the server has already done — an
  /// account created, a session started, a family revoked — and both are followed
  /// by the navigation that tells the customer it worked. An exception here (a
  /// platform channel, a browser refusing storage, a full disk) would escape into
  /// a `catch (ApiFailure)` that does not catch it, and the screen would sit on
  /// its spinner for ever over a registration that actually succeeded.
  ///
  /// Failing to remember costs one extra tap at the next launch. Failing to
  /// navigate costs the account.
  Future<void> _remember(bool chosen) async {
    if (state != chosen) state = chosen;

    try {
      if (chosen) {
        await _preferences?.setBool(_key, true);
      } else {
        await _preferences?.remove(_key);
      }
    } on Object {
      // See above. The in-memory answer is already correct for this run.
    }
  }
}

/// A device whose preferences could not be read is treated as a fresh install.
///
/// That is the recoverable way round: Get Started is one tap from browsing, while
/// the other default would mean a genuine first run never sees it at all.
final entryChoiceProvider = StateNotifierProvider<EntryChoice, bool>(
  (ref) => EntryChoice(ref.watch(sharedPreferencesProvider)),
);

// ── Platform configuration ─────────────────────────────────────────────────────

/// Read once at startup and held for the session.
///
/// Everything on it is a value the server owns and the app would otherwise have to
/// hard-code — the time zone the calendar runs in, the currency's decimals, the
/// date picker's three bounds, the words on every filter chip. `keepAlive` because
/// a screen without it cannot render a price or a date at all.
final appConfigProvider = FutureProvider<AppConfig>((ref) async {
  ref.keepAlive();
  return ref.watch(apiProvider).appConfig();
});

/// What the platform will accept as a password, or null until it has said.
///
/// Its own provider because three screens ask the same question — register,
/// reset and change — and each of them would otherwise reach into the config the
/// same way. Null while the config is in flight, and the validator's contract is
/// that null means "let the server judge", never "assume the old default".
final passwordPolicyProvider = Provider<PasswordPolicy?>(
  (ref) => ref.watch(appConfigProvider).valueOrNull?.password,
);

/// Whether this build is talking to a server that takes no real money.
///
/// False until the config has arrived, and false for every value the app does not
/// recognise. That asymmetry is the point: a banner missed on a test host is a
/// nuisance, and a banner shown to a paying customer tells them their payment was
/// fake. Silence is the safe direction, so silence is the default.
final sandboxPaymentsProvider = Provider<bool>(
  (ref) => ref.watch(appConfigProvider).valueOrNull?.payments.isSandbox ?? false,
);

final citiesProvider = FutureProvider<List<Lookup>>((ref) async {
  ref.keepAlive();
  return ref.watch(apiProvider).cities();
});

final carTypesProvider = FutureProvider<List<Lookup>>((ref) async {
  ref.keepAlive();
  return ref.watch(apiProvider).carTypes();
});

/// Money and dates, in the reader's language and the platform's zone.
///
/// Depends on BOTH the config and the locale, so switching language re-derives
/// every formatted string without a screen having to know it happened.
final formatsProvider = Provider<Formats?>((ref) {
  final config = ref.watch(appConfigProvider).valueOrNull;
  if (config == null) return null;

  // The SAME answer the framework lays the screen out with. Reading
  // `platformDispatcher.locale` here instead is what put English dates on an
  // Arabic screen for a device asking for neither.
  final locale = resolveKhadraLocale(
    ref.watch(localeProvider),
    WidgetsBinding.instance.platformDispatcher.locales,
  );

  return Formats(
    locale: locale.languageCode,
    currency: config.currency,
    // Falls back to UTC rather than throwing if the server names a zone this
    // build's database does not carry. A wrong-by-hours date is bad; a screen
    // that cannot render at all is worse.
    zone: _location(config.timeZone),
  );
});

tz.Location _location(String name) {
  try {
    return tz.getLocation(name);
  } on Object {
    return tz.UTC;
  }
}

/// Whether the app is currently showing Arabic. Used where a DTO carries both
/// languages and the widget has to pick one.
final isArabicProvider = Provider<bool>((ref) {
  final locale = resolveKhadraLocale(
    ref.watch(localeProvider),
    WidgetsBinding.instance.platformDispatcher.locales,
  );
  return locale.languageCode == 'ar';
});

/// The name of one city, in the reader's language, or null.
///
/// The catalogue carries a city as an ID — a listing row and a gallery page both
/// do — and the NAME lives on the lookup, in both languages. Null covers three
/// different things and all three mean the same to a screen: no city on the
/// record, a city the lookup has not loaded yet, and an id the lookup does not
/// know. Rendering the raw GUID for any of them would be worse than rendering
/// nothing, so the caller omits the line.
final cityNameProvider = Provider.family<String?, String?>((ref, cityId) {
  if (cityId == null || cityId.isEmpty) return null;

  final cities = ref.watch(citiesProvider).valueOrNull;
  if (cities == null) return null;

  final arabic = ref.watch(isArabicProvider);
  for (final city in cities) {
    if (city.id == cityId) {
      final name = city.nameFor(arabic);
      return name.isEmpty ? null : name;
    }
  }
  return null;
});
