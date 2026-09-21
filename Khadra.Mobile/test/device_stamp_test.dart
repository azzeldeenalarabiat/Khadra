import 'package:flutter_test/flutter_test.dart';
import 'package:khadra_mobile/core/config/device_stamp.dart';

/// What this build tells the server it is running on.
///
/// The value lands in `refresh_tokens.user_agent` and comes back on Registered
/// Devices, so it has two jobs and they pull against each other: say enough for
/// a customer to recognise their own phone, and say nothing that identifies it
/// to anybody else.
///
/// The first version failed both. It passed `Platform.operatingSystemVersion`
/// through, which on Android is the build description — product codename, build
/// id, incremental build number, signing tags — and the devices screen showed
/// `…sdk_gphone64_x86_64-userd` where the phone's name should be. Caught on an
/// emulator, not by a unit test, which is why there is one now.
void main() {
  test('names the platform and a version, and nothing else', () {
    final stamp = deviceStamp();

    // On the host that runs these tests that is Windows, macOS or Linux; the
    // shape is what matters and it is the same shape on a phone.
    expect(stamp, isNotEmpty);
    expect(
      RegExp(r'^(Android|iOS|macOS|Windows|Linux)( \d{1,3}(\.\d{1,5}){0,2})?$').hasMatch(stamp),
      isTrue,
      reason: '"$stamp" is not "<platform>" or "<platform> <version>"',
    );
  });

  test('carries no build identifiers', () {
    final stamp = deviceStamp();

    // The four things the Android build description leaks, none of which
    // belongs on a row a customer reads or in a column an attacker might.
    for (final leak in ['userdebug', 'release-keys', 'dev-keys', 'sdk_gphone']) {
      expect(stamp.contains(leak), isFalse, reason: 'the stamp leaked "$leak"');
    }
    // Short enough to render on one line of a phone-width card.
    expect(stamp.length, lessThanOrEqualTo(24));
  });
}
