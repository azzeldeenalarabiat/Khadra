import 'package:dio/dio.dart';

/// Tells the API which language to answer in, on every request.
///
/// **The app's own language, never the phone's.** A customer who set Khadra to
/// Arabic on an English handset is reading Arabic, and the dealer-written text the
/// server chooses has to match the screen it lands on — otherwise one paragraph of
/// a car's page arrives in the other language for no reason the reader can see.
/// The value is whatever `resolveKhadraLocale` settled on, which is the language
/// actually being rendered: a deliberate choice when there is one, and the device's
/// only after it has been clamped to one of the two this app speaks.
///
/// An interceptor rather than a header baked into `BaseOptions`, because the
/// language changes while the app is running — the switch in Profile turns the
/// whole app without a restart — and a header set once at construction would keep
/// asking for the language the app opened in for the rest of the process.
///
/// A request that sets its own `Accept-Language` keeps it. Nothing does today; the
/// exception exists so a future caller that genuinely needs another language does
/// not have to fight this.
class LanguageInterceptor extends Interceptor {
  LanguageInterceptor(this.language);

  /// Read at request time, not captured: see above.
  final String Function() language;

  static const header = 'Accept-Language';

  @override
  void onRequest(RequestOptions options, RequestInterceptorHandler handler) {
    final already = options.headers.keys
        .any((name) => name.toLowerCase() == header.toLowerCase());
    if (!already) {
      // A bare tag, with no quality list. The server reads the first acceptable
      // one and falls back to English for anything it does not speak, so there is
      // nothing for a `q=` to express here.
      options.headers[header] = language();
    }
    handler.next(options);
  }
}
