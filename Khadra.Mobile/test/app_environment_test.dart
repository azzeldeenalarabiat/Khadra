import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:khadra_mobile/core/config/app_environment.dart';

/// Which server a build talks to, and the two refusals that keep production and staging apart.
///
/// The staging build exists so a tester knows which server they are on. Every rule here is one way
/// that promise could quietly break: a staging app pointed at production, a production app pointed
/// at staging, or an Android identity that lets one install over the other.
void main() {
  const production = 'https://khadra.onrender.com';
  const staging = AppEnvironment.stagingApiBaseUrl;
  const loopback = 'http://10.0.2.2:5012';

  group('the flavor names the build', () {
    test('no flavor, and "production", are the customer app', () {
      expect(AppEnvironment.buildFor(null), KhadraBuild.production);
      expect(AppEnvironment.buildFor(''), KhadraBuild.production);
      expect(AppEnvironment.buildFor('production'), KhadraBuild.production);
    });

    test('"staging" is the test app', () {
      expect(AppEnvironment.buildFor('staging'), KhadraBuild.staging);
    });

    test('an unknown flavor is a build mistake, not a guess', () {
      expect(() => AppEnvironment.buildFor('dev'), throwsStateError);
      expect(() => AppEnvironment.buildFor('Staging'), throwsStateError);
    });

    test('a test process is not the staging build', () {
      expect(AppEnvironment.isStaging, isFalse);
    });
  });

  group('staging talks to staging and nothing else', () {
    String resolve(String override) => AppEnvironment.resolveApiBaseUrl(
        build: KhadraBuild.staging, override: override, developmentDefault: loopback);

    test('the address is compiled in; no flag is needed', () {
      expect(staging, 'https://khadra-staging.onrender.com');
      expect(resolve(''), staging);
    });

    test('naming the staging address explicitly is harmless', () {
      expect(resolve(staging), staging);
      expect(resolve('$staging/'), staging);
    });

    test('pointing it at production is refused', () {
      expect(() => resolve(production), throwsStateError);
    });

    test('pointing it at a laptop is refused too', () {
      expect(() => resolve(loopback), throwsStateError);
    });
  });

  group('production is exactly what it was', () {
    String resolve(String override) => AppEnvironment.resolveApiBaseUrl(
        build: KhadraBuild.production, override: override, developmentDefault: loopback);

    test('the release flag decides the address, trailing slash dropped', () {
      expect(resolve(production), production);
      expect(resolve('$production/'), production);
    });

    test('with no flag it is the development loopback, never a compiled-in production URL', () {
      expect(resolve(''), loopback);
    });

    test('pointing it at staging is refused', () {
      expect(() => resolve(staging), throwsStateError);
      expect(() => resolve('$staging/'), throwsStateError);
    });
  });

  group('the Android identities cannot collide', () {
    final gradle = File('android/app/build.gradle.kts').readAsStringSync();

    test('staging is a separate application, production keeps its id', () {
      expect(gradle, contains('applicationId = "com.khadra.khadra_mobile"'));
      final stagingBlock = RegExp(r'create\("staging"\)\s*\{([^}]*)\}').firstMatch(gradle)!.group(1)!;
      expect(stagingBlock, contains('applicationIdSuffix = ".staging"'));
      final productionBlock =
          RegExp(r'create\("production"\)\s*\{([^}]*)\}').firstMatch(gradle)!.group(1)!;
      expect(productionBlock, isNot(contains('applicationIdSuffix')));
    });

    // "1.1.0-staging" is a PRERELEASE of 1.1.0 and ranks below it, so the API's minimum-version
    // gate would refuse the staging app on every request.
    test('no flavor alters the version the API compares', () {
      expect(gradle, isNot(contains(RegExp(r'versionNameSuffix\s*='))));
    });

    test('the default flavor is production, so the old release command still builds it', () {
      final pubspec = File('pubspec.yaml').readAsStringSync();
      expect(pubspec, contains(RegExp(r'^\s+default-flavor: production\s*$', multiLine: true)));
    });

    test('the staging launcher says TEST in both languages', () {
      for (final dir in ['values', 'values-ar']) {
        final strings = File('android/app/src/staging/res/$dir/strings.xml').readAsStringSync();
        expect(strings, contains(RegExp(r'<string name="app_name">[^<]*TEST</string>')),
            reason: dir);
      }
    });

    test('production keeps its own name', () {
      final strings = File('android/app/src/main/res/values/strings.xml').readAsStringSync();
      expect(strings, contains('<string name="app_name">Khadra</string>'));
    });
  });
}
