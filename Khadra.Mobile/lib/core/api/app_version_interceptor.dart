import 'package:dio/dio.dart';

import '../config/app_version.dart';
import '../config/update_requirement.dart';

/// Tells the API which build is calling, and notices when the API says that build
/// is too old.
///
/// **The header** is `X-Khadra-App-Version: 1.1.0+2` — the installed version as
/// the platform reports it, build number included so a log can tell builds apart
/// (the API ignores everything after `+`). It is how the API's version gate knows
/// this build can read today's contracts: without it, a request carrying this
/// app's User-Agent is taken for a build that predates the header, and refused.
/// Sent to the API's own origin ONLY. A server-minted upload URL on another host
/// must not be handed a header about the app — and on the web it is not a
/// CORS-safelisted header, so it would force a preflight against a host this app
/// does not control.
///
/// **The refusal** is `426` with `code: app.update_required`. Seen on ANY call,
/// it raises [onUpdateRequired], which puts the update screen in front of the
/// whole app — so a minimum raised while the app is open takes effect on the next
/// call, not the next launch. The error still travels on to its caller: the
/// screen that asked gets its failure, and the update screen covers it.
class AppVersionInterceptor extends Interceptor {
  AppVersionInterceptor({
    required this.installedVersion,
    required this.apiBaseUrl,
    required this.onUpdateRequired,
  });

  /// `1.1.0+2`, or null when the platform would not say. With no version the
  /// header is left off rather than invented, and the API refuses this phone
  /// like any unversioned build — which shows the update screen. A made-up
  /// version would let a build nobody can identify through the gate.
  final String? installedVersion;

  final String apiBaseUrl;

  final void Function(UpdateRequirement requirement) onUpdateRequired;

  static const header = 'X-Khadra-App-Version';

  late final Uri _api = Uri.parse(apiBaseUrl);

  @override
  void onRequest(RequestOptions options, RequestInterceptorHandler handler) {
    final version = installedVersion;
    if (version != null && _sameOrigin(options.uri, _api)) {
      options.headers[header] = version;
    }
    handler.next(options);
  }

  @override
  void onError(DioException err, ErrorInterceptorHandler handler) {
    final requirement = UpdateRequirement.fromRefusal(
      err.response?.data,
      installed: AppVersion.tryParse(installedVersion),
    );
    if (requirement != null) onUpdateRequired(requirement);
    handler.next(err);
  }

  static bool _sameOrigin(Uri request, Uri api) =>
      request.scheme == api.scheme &&
      request.host == api.host &&
      request.port == api.port;
}
