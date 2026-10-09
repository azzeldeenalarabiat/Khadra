import 'package:dio/dio.dart';

/// Notices when the API says this person has a legal text in force still to accept
/// (pre-launch items 224 and 238).
///
/// The refusal is `403` with `code: legal.consent_pending`, and from 1.4.0 the API
/// sends it to this app on every signed-in call except the few that resolve it. Seen
/// on ANY call, it raises [onConsentPending], which puts the consent prompt in front
/// of the app. Keyed on the CODE, never the status: a 403 for a role or a policy is
/// not a reason to ask for consent.
///
/// The error still travels on to its caller, as the update interceptor's does: the
/// screen that asked gets its failure, and the prompt covers it. Raising the prompt
/// twice is harmless; it is one flag.
class ConsentInterceptor extends Interceptor {
  ConsentInterceptor({required this.onConsentPending});

  final void Function() onConsentPending;

  static const consentPendingCode = 'legal.consent_pending';

  @override
  void onError(DioException err, ErrorInterceptorHandler handler) {
    final body = err.response?.data;
    if (err.response?.statusCode == 403 && body is Map && body['code'] == consentPendingCode) {
      onConsentPending();
    }
    handler.next(err);
  }
}
