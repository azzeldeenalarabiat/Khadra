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

final sessionStoreProvider = Provider<SessionStore>((ref) => SessionStore());

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

final sharedPreferencesProvider = Provider<SharedPreferences?>(
  (ref) => throw UnimplementedError('Overridden in main() once loaded.'),
);

final localeProvider = StateNotifierProvider<LocaleController, Locale?>(
  (ref) => LocaleController(ref.watch(sharedPreferencesProvider)),
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

  final locale = ref.watch(localeProvider)?.languageCode ??
      WidgetsBinding.instance.platformDispatcher.locale.languageCode;

  return Formats(
    locale: locale == 'ar' ? 'ar' : 'en',
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
  final locale = ref.watch(localeProvider)?.languageCode ??
      WidgetsBinding.instance.platformDispatcher.locale.languageCode;
  return locale == 'ar';
});
