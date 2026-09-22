import 'app_version.dart';

/// This build must be updated before it can be used, and what the update screen
/// can say about it.
///
/// Raised in one of two ways, and both end at the same screen: the installed
/// version is below the minimum `/app-config` publishes, or the API refused a call
/// with `426 app.update_required` — which also covers a minimum raised while the
/// app was already open.
class UpdateRequirement {
  const UpdateRequirement({this.installed, this.minimum, this.updateUrl});

  /// Null when the platform would not say which version is installed.
  final AppVersion? installed;

  /// Null when all that is known is the server's refusal and it named none.
  final AppVersion? minimum;

  /// Where the current build is published. Null when nowhere has been named.
  final Uri? updateUrl;

  /// The refusal body, when it is one. Keyed on the CODE, never on the status:
  /// a 426 from anything else — a proxy, a future use of the status — is not a
  /// reason to lock the app.
  static UpdateRequirement? fromRefusal(Object? body, {AppVersion? installed}) {
    if (body is! Map || body['code'] != updateRequiredCode) return null;
    final link = Uri.tryParse(body['updateUrl'] as String? ?? '');
    return UpdateRequirement(
      installed: installed,
      minimum: AppVersion.tryParse(body['minimumSupportedVersion'] as String?),
      updateUrl: link != null && (link.scheme == 'https' || link.scheme == 'http')
          ? link
          : null,
    );
  }

  /// The API's code for a build it no longer serves
  /// (`Khadra.Application/Common/MobileAppContract.cs`).
  static const updateRequiredCode = 'app.update_required';
}
