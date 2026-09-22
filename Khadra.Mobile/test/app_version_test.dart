import 'dart:convert';
import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:khadra_mobile/core/config/app_version.dart';

/// How two builds of this app are ordered, on the phone's side of the gate.
///
/// The cases are read from `docs/contracts/app-version-vectors.json` — the SAME
/// file the API's `AppVersionTests` reads. The gate works only if the server and
/// the phone agree about which of two builds is newer, and two lists typed out
/// twice would agree today and drift the first time one side learned a new case.
void main() {
  final vectors = jsonDecode(
    File('${Directory.current.parent.path}/docs/contracts/app-version-vectors.json')
        .readAsStringSync(),
  ) as Map<String, dynamic>;

  AppVersion parse(String text) {
    final version = AppVersion.tryParse(text);
    expect(version, isNotNull, reason: '"$text" should parse');
    return version!;
  }

  group('every pair in an ascending list is ordered, both ways', () {
    for (final list in vectors['ascending'] as List<dynamic>) {
      final ascending = (list as List<dynamic>).cast<String>();
      test(ascending.join(' < '), () {
        // Every pair, not only neighbours: the gate compares an arbitrary build
        // with an arbitrary minimum.
        for (var lower = 0; lower < ascending.length; lower++) {
          for (var higher = lower + 1; higher < ascending.length; higher++) {
            final a = parse(ascending[lower]);
            final b = parse(ascending[higher]);
            expect(a < b, isTrue, reason: '${ascending[lower]} < ${ascending[higher]}');
            expect(b > a, isTrue, reason: '${ascending[higher]} > ${ascending[lower]}');
            expect(a.compareTo(b), lessThan(0));
            expect(a == b, isFalse);
          }
        }
      });
    }
  });

  group('build metadata never changes the order', () {
    for (final pair in vectors['equal'] as List<dynamic>) {
      final [left, right] = (pair as List<dynamic>).cast<String>();
      test('$left == $right', () {
        final a = parse(left);
        final b = parse(right);
        expect(a.compareTo(b), 0);
        expect(a <= b && a >= b, isTrue);
        expect(a == b, isTrue);
        expect(a.hashCode, b.hashCode);
      });
    }
  });

  group('anything that is not a semantic version is refused', () {
    for (final text in (vectors['invalid'] as List<dynamic>).cast<String>()) {
      test(jsonEncode(text), () => expect(AppVersion.tryParse(text), isNull));
    }
  });

  test('numbers are compared as numbers, never as text', () {
    // The reason the type exists: as strings "1.10.0" sorts below "1.9.0", and a
    // minimum of 1.9.0 would lock out every build from 1.10 onwards.
    expect('1.10.0'.compareTo('1.9.0'), lessThan(0));
    expect(parse('1.10.0') > parse('1.9.0'), isTrue);
  });

  test('a version prints without its build metadata', () {
    expect(parse('1.1.0+2').toString(), '1.1.0');
    expect(parse('1.1.0-rc.1+7').toString(), '1.1.0-rc.1');
    expect(AppVersion.tryParse(null), isNull);
  });
}
