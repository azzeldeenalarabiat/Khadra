import 'dart:io';

/// What this device is, on a platform that has an operating system to ask.
///
/// The operating system and its version, and NOTHING that identifies the
/// handset: no model, no serial, no advertising id, no installation id. The
/// value is stored on every refresh token and shown back on Registered Devices,
/// so it has to be enough for a customer to answer "was that me?" and no more
/// than that — a row that names the phone in somebody's pocket is a row worth
/// stealing.
///
/// `Platform.operatingSystemVersion` is NOT that string, which is why this
/// parses rather than passes it through. On Android it is the build
/// description:
///
///     sdk_gphone64_x86_64-userdebug 16 BE2A.250530.026.F3 13894323 dev-keys
///
/// — the product codename, the build id, the incremental build NUMBER and the
/// signing tags. That was what the first version of this sent, and Registered
/// Devices duly showed it, ellipsised, on the row meant to say "your phone". It
/// is unreadable, and the incremental is narrower than the device model it was
/// avoiding. iOS is the other shape, `Version 18.2 (Build 22C152)`, useless in
/// the other direction.
///
/// So: the platform's own name, and the first thing in that string that looks
/// like a version. "Android 16". "iOS 18.2". Nothing when neither can be found,
/// which the screen already words as "this device" or "unrecognised device".
String deviceStamp() {
  final name = switch (Platform.operatingSystem) {
    'android' => 'Android',
    'ios' => 'iOS',
    'macos' => 'macOS',
    'windows' => 'Windows',
    'linux' => 'Linux',
    final other => other.isEmpty ? '' : other[0].toUpperCase() + other.substring(1),
  };
  if (name.isEmpty) return '';

  final version = _version(Platform.operatingSystemVersion);
  return version == null ? name : '$name $version';
}

/// The first token that reads as a version number: `16`, `18.2`, `10.0.26200`.
///
/// Bounded at three parts, because a fourth is a build number rather than a
/// version, and that is the part worth leaving out.
String? _version(String raw) {
  final match = RegExp(r'(?<![\w.])(\d{1,3}(?:\.\d{1,5}){0,2})(?![\w.])').firstMatch(raw);
  return match?.group(1);
}
