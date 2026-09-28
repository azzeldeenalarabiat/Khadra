import 'dart:convert';
import 'dart:io';
import 'dart:math' as math;

import 'package:flutter_test/flutter_test.dart';
import 'package:image/image.dart' as img;
import 'package:khadra_mobile/core/theme/khadra_theme.dart';
import 'package:khadra_mobile/l10n/app_localizations.dart';
import 'package:khadra_mobile/l10n/app_localizations_ar.dart';
import 'package:khadra_mobile/l10n/app_localizations_en.dart';

import 'support/pbxproj.dart';

/// The iOS project, checked on a machine that cannot build it.
///
/// Nothing in this repository's everyday loop runs Xcode: the app is written and
/// tested on Windows, and iOS is built only by `.github/workflows/ios-build.yml` on a
/// macOS runner. A scheme naming a configuration that does not exist, a bundle id set
/// where it overrides the flavor, or a plugin needing a newer iOS than the project
/// targets would otherwise surface after a push and a wait for a Mac. These read the
/// files Xcode reads and find all of that here, in seconds; the workflow then checks
/// the same facts on the app it actually built.
void main() {
  late final project = Pbxproj.parse(File('ios/Runner.xcodeproj/project.pbxproj').readAsStringSync());
  late final flavors = _androidFlavors();
  const modes = ['Debug', 'Profile', 'Release'];
  late final configurations = {
    for (final flavor in flavors.keys)
      for (final mode in modes) '$mode-$flavor',
  };

  group('the Xcode project', () {
    test('reads as a property list, and every object it names exists', () {
      expect(project.duplicateKeys, isEmpty,
          reason: 'a key appears twice in one dictionary; Xcode keeps only the last');

      final defined = project.objects.keys.toSet();
      final referenced = project.referencedIds;
      expect(referenced.difference(defined), isEmpty,
          reason: 'these ids are named but never defined — xcodebuild refuses such a project');
      expect(defined.difference(referenced), isEmpty,
          reason: 'these objects are defined but nothing refers to them — a left-over from an edit');
    });

    test('builds the same flavors Android does, and nothing unflavored', () {
      // The flavor is the ONE selector, as on Android: an unflavored configuration
      // would build the customer app's id with no API address behind it.
      expect(flavors.keys, containsAll(['production', 'staging']));

      // Flutter's Xcode build step reads the build mode out of the configuration's
      // name by substring — release, then profile, then debug — so a flavor called
      // "prerelease" would build Debug-prerelease as a RELEASE build.
      for (final flavor in flavors.keys) {
        expect(flavor.toLowerCase(), isNot(matches(RegExp('release|profile|debug'))),
            reason: 'the flavor "$flavor" would be misread as a build mode');
      }

      final owners = {
        'the project': project.project,
        'Runner': project.target('Runner').value,
        'RunnerTests': project.target('RunnerTests').value,
      };
      for (final MapEntry(key: owner, value: settings) in owners.entries) {
        final names = project.configurationsOf(settings).map((c) => c['name']).toSet();
        expect(names, configurations,
            reason: '$owner must have exactly <Debug|Profile|Release>-<flavor> for every flavor');
        expect(configurations, contains(project.configurationListOf(settings)['defaultConfigurationName']),
            reason: "$owner's default configuration must be one that exists");
      }
    });

    test('takes each flavor from its own files, never from project.pbxproj', () {
      // A setting written in the target overrides the xcconfig under it, so a
      // bundle id here would make both flavors the same app while every file said
      // otherwise.
      for (final configuration in project.configurationsOf(project.target('Runner').value)) {
        final name = configuration['name']! as String;
        final flavor = name.split('-').last;
        final settings = configuration['buildSettings']! as Map<String, Object>;
        for (final key in ['PRODUCT_BUNDLE_IDENTIFIER', 'KHADRA_FLAVOR', 'KHADRA_DISPLAY_NAME']) {
          expect(settings.containsKey(key), isFalse,
              reason: '$key in Runner / $name overrides ios/Flutter/$flavor.xcconfig');
        }

        final base = project.object(configuration['baseConfigurationReference']! as String);
        expect(base['path'], 'Flutter/$name.xcconfig', reason: 'Runner / $name is based on the wrong file');
        final includes = _includes('ios/Flutter/$name.xcconfig');
        expect(includes, [
          'Pods/Target Support Files/Pods-Runner/Pods-Runner.${name.toLowerCase()}.xcconfig',
          'Generated.xcconfig',
          '$flavor.xcconfig',
        ], reason: 'ios/Flutter/$name.xcconfig must bring in its own pods settings, then '
            "Flutter's, then its flavor's — in that order, so the flavor has the last word");
      }
    });

    test('makes the two flavors two apps, told apart the way Android tells them apart', () {
      final production = _xcconfig('ios/Flutter/production.xcconfig');
      final staging = _xcconfig('ios/Flutter/staging.xcconfig');

      // The owner's choice (2026-09-28). Changing it is free until App Store
      // Connect holds a record for it, and impossible afterwards — so a change
      // must be deliberate enough to edit this line too.
      expect(production['PRODUCT_BUNDLE_IDENTIFIER'], 'com.khadra.app');
      expect(staging['PRODUCT_BUNDLE_IDENTIFIER'],
          '${production['PRODUCT_BUNDLE_IDENTIFIER']}${flavors['staging']}',
          reason: "staging's id is the customer app's plus Android's own applicationIdSuffix");

      for (final flavor in flavors.keys) {
        final settings = _xcconfig('ios/Flutter/$flavor.xcconfig');
        expect(settings['KHADRA_FLAVOR'], flavor,
            reason: 'the build phase that installs the Arabic names reads KHADRA_FLAVOR');
      }

      for (final configuration in project.configurationsOf(project.target('RunnerTests').value)) {
        final flavor = (configuration['name']! as String).split('-').last;
        final app = _xcconfig('ios/Flutter/$flavor.xcconfig')['PRODUCT_BUNDLE_IDENTIFIER'];
        expect((configuration['buildSettings']! as Map)['PRODUCT_BUNDLE_IDENTIFIER'], '$app.RunnerTests',
            reason: "a test bundle's id sits under the app it tests");
      }
    });

    test('has one shared scheme per flavor, whose every action builds that flavor', () {
      final schemes = Directory('ios/Runner.xcodeproj/xcshareddata/xcschemes')
          .listSync()
          .map((file) => file.uri.pathSegments.last)
          .toSet();
      expect(schemes, {for (final flavor in flavors.keys) '$flavor.xcscheme'},
          reason: '`flutter build ios --flavor <flavor>` looks for a scheme of that name, and any '
              'other scheme would build a configuration that is no flavor at all');

      final runner = project.target('Runner').key;
      final tests = project.target('RunnerTests').key;
      for (final flavor in flavors.keys) {
        final scheme = File('ios/Runner.xcodeproj/xcshareddata/xcschemes/$flavor.xcscheme').readAsStringSync();
        String action(String name) =>
            RegExp('<$name\\b[^>]*?\\bbuildConfiguration\\s*=\\s*"([^"]*)"').firstMatch(scheme)?.group(1) ?? '';
        expect(
          {
            for (final name in ['TestAction', 'LaunchAction', 'AnalyzeAction', 'ProfileAction', 'ArchiveAction'])
              name: action(name),
          },
          {
            'TestAction': 'Debug-$flavor',
            'LaunchAction': 'Debug-$flavor',
            'AnalyzeAction': 'Debug-$flavor',
            'ProfileAction': 'Profile-$flavor',
            'ArchiveAction': 'Release-$flavor',
          },
        );
        // Flutter's own pre-build step, which its Swift Package Manager support
        // requires to be in the scheme it builds.
        expect(scheme, contains('xcode_backend.sh&quot; prepare'));
        expect(scheme, contains('BlueprintIdentifier = "$runner"'));
        expect(scheme, contains('BlueprintIdentifier = "$tests"'));
      }

      final defaultFlavor = RegExp(r'^\s*default-flavor:\s*(\S+)', multiLine: true)
          .firstMatch(File('pubspec.yaml').readAsStringSync())
          ?.group(1);
      expect(flavors.keys, contains(defaultFlavor),
          reason: "pubspec.yaml's default-flavor is what a plain `flutter build ios` builds");
    });

    test('targets one iOS version, the Podfile agrees, and no plugin needs a newer one', () {
      final targets = {
        for (final configuration in project.configurationsOf(project.project))
          (configuration['buildSettings']! as Map)['IPHONEOS_DEPLOYMENT_TARGET'],
        for (final name in ['Runner', 'RunnerTests'])
          for (final configuration in project.configurationsOf(project.target(name).value))
            if ((configuration['buildSettings']! as Map)['IPHONEOS_DEPLOYMENT_TARGET'] case final value?) value,
      };
      expect(targets, hasLength(1), reason: 'every configuration must target the same iOS');
      final target = _Version.parse(targets.single! as String);

      final podfile = File('ios/Podfile').readAsStringSync();
      final platform = RegExp(r"^platform :ios, '([\d.]+)'", multiLine: true).firstMatch(podfile)?.group(1);
      expect(platform, isNotNull, reason: 'the Podfile must name its platform version');
      expect(_Version.parse(platform!), target, reason: 'ios/Podfile and project.pbxproj disagree');

      // Every native plugin is linked into the app whether or not it runs on
      // iOS, so the one that needs the newest iOS sets the floor. Read from each
      // plugin's own Package.swift and podspec, where the Mac build reads it.
      final minimums = _pluginMinimums();
      expect(minimums, isNotEmpty, reason: 'no plugin minimum could be read, so this proved nothing');
      final tooNew = {
        for (final MapEntry(key: plugin, value: minimum) in minimums.entries)
          if (minimum.compareTo(target) > 0) plugin: '$minimum',
      };
      expect(tooNew, isEmpty,
          reason: 'these plugins need a newer iOS than $target — raise IPHONEOS_DEPLOYMENT_TARGET '
              'in every project configuration and `platform :ios` in ios/Podfile together');
    });

    test('tells CocoaPods which configurations are debug builds', () {
      final podfile = File('ios/Podfile').readAsStringSync();
      final mapping = {
        for (final match in RegExp(r"'([^']+)'\s*=>\s*:(debug|release)").allMatches(podfile))
          match.group(1)!: match.group(2)!,
      };
      expect(mapping, {
        for (final configuration in configurations)
          configuration: configuration.startsWith('Debug-') ? 'debug' : 'release',
      });
    });
  });

  group('the app on the home screen', () {
    test("carries the flavor's name, in English and Arabic, the same as the Android launcher's", () {
      final plist = File('ios/Runner/Info.plist').readAsStringSync();
      expect(_plistString(plist, 'CFBundleDisplayName'), r'$(KHADRA_DISPLAY_NAME)');
      expect(_plistString(plist, 'CFBundleName'), r'$(KHADRA_DISPLAY_NAME)');
      expect(_plistString(plist, 'CFBundleIdentifier'), r'$(PRODUCT_BUNDLE_IDENTIFIER)');

      // Each flavor's names on iOS against the same flavor's on Android: the
      // Latin one from the xcconfig Info.plist expands, the Arabic one from the
      // strings the "Copy Flavor Localizations" phase installs.
      final androidRes = {
        'production': 'android/app/src/main/res',
        'staging': 'android/app/src/staging/res',
      };
      for (final flavor in flavors.keys) {
        final latin = _xcconfig('ios/Flutter/$flavor.xcconfig')['KHADRA_DISPLAY_NAME'];
        final arabic = _strings('ios/Runner/Flavors/$flavor/ar.lproj/InfoPlist.strings');
        expect(latin, _androidAppName('${androidRes[flavor]}/values/strings.xml'), reason: flavor);
        expect(arabic['CFBundleDisplayName'], _androidAppName('${androidRes[flavor]}/values-ar/strings.xml'),
            reason: flavor);
        expect(arabic['CFBundleName'], arabic['CFBundleDisplayName'], reason: flavor);
      }

      // And the customer app's name is the one the app gives itself once open.
      expect(_xcconfig('ios/Flutter/production.xcconfig')['KHADRA_DISPLAY_NAME'], AppLocalizationsEn().appName);
      expect(_strings('ios/Runner/Flavors/production/ar.lproj/InfoPlist.strings')['CFBundleDisplayName'],
          AppLocalizationsAr().appName);
    });

    test('declares the languages the app speaks, so iOS offers them and speaks them too', () {
      // Without CFBundleLocalizations an iPhone set to Arabic runs the app's
      // system sheets — the photo picker, the file picker, the permission alert
      // — in English, and Settings offers no per-app language.
      final plist = File('ios/Runner/Info.plist').readAsStringSync();
      expect(_plistArray(plist, 'CFBundleLocalizations').toSet(),
          {for (final locale in AppLocalizations.supportedLocales) locale.languageCode});

      final known = (project.project['knownRegions']! as List).toSet();
      for (final locale in AppLocalizations.supportedLocales) {
        expect(known, contains(locale.languageCode));
      }
    });

    test('asks only for what the document picker uses, and explains it in Arabic too', () {
      final plist = File('ios/Runner/Info.plist').readAsStringSync();
      final asked = {
        for (final match in RegExp(r'<key>(NS\w+UsageDescription)</key>').allMatches(plist)) match.group(1)!,
      };
      // The camera and the photo library, both from `DocumentPicker`: the licence,
      // the identity document and a dispute's photos. image_picker asks for the
      // library key even though its picker needs no permission — App Store policy
      // requires it. No location: the map shows an office, it never finds the
      // phone. Anything new here needs a reason, and an Arabic text.
      expect(asked, {'NSCameraUsageDescription', 'NSPhotoLibraryUsageDescription'});

      final productionStrings = _strings('ios/Runner/Flavors/production/ar.lproj/InfoPlist.strings');
      for (final flavor in flavors.keys) {
        final arabic = _strings('ios/Runner/Flavors/$flavor/ar.lproj/InfoPlist.strings');
        for (final key in asked) {
          expect(arabic[key], isNot(anyOf(isNull, isEmpty)), reason: '$flavor has no Arabic $key');
          expect(arabic[key], isNot(_plistString(plist, key)), reason: '$flavor: $key is still English');
        }
        // The flavors differ in their NAME, and in nothing else.
        expect({
          for (final MapEntry(:key, :value) in arabic.entries)
            if (!key.startsWith('CFBundle')) key: value,
        }, {
          for (final MapEntry(:key, :value) in productionStrings.entries)
            if (!key.startsWith('CFBundle')) key: value,
        }, reason: '$flavor must differ from production in its name only');
      }
    });

    test('installs those strings after the bundle resources, from the flavor being built', () {
      final runner = project.target('Runner').value;
      final phases = [for (final id in runner['buildPhases']! as List) project.object(id as String)];
      final resources = phases.indexWhere((phase) => phase['isa'] == 'PBXResourcesBuildPhase');
      final copy = phases.indexWhere((phase) => phase['name'] == 'Copy Flavor Localizations');
      expect(copy, greaterThan(resources),
          reason: 'the phase must run after Copy Bundle Resources, or nothing is there to add to');
      expect(phases[copy]['shellScript'], contains(r'Runner/Flavors/${KHADRA_FLAVOR}'));

      // It reads files it does not declare as inputs, which a sandboxed script may not.
      for (final configuration in project.configurationsOf(project.project)) {
        expect((configuration['buildSettings']! as Map)['ENABLE_USER_SCRIPT_SANDBOXING'], 'NO',
            reason: '${configuration['name']}');
      }

      // Every flavor supplies every language beyond Info.plist's own English.
      final languages = {
        for (final locale in AppLocalizations.supportedLocales)
          if (locale.languageCode != 'en') '${locale.languageCode}.lproj',
      };
      for (final flavor in flavors.keys) {
        final folders = Directory('ios/Runner/Flavors/$flavor')
            .listSync()
            .whereType<Directory>()
            .map((folder) => folder.uri.pathSegments.where((s) => s.isNotEmpty).last)
            .toSet();
        expect(folders, languages, reason: flavor);
      }
    });

    test('shows an icon at every size the set names, with no alpha channel', () {
      const set = 'ios/Runner/Assets.xcassets/AppIcon.appiconset';
      final images = ((jsonDecode(File('$set/Contents.json').readAsStringSync()) as Map)['images'] as List)
          .cast<Map<String, dynamic>>();
      expect(images, isNotEmpty);
      for (final entry in images) {
        final name = entry['filename'] as String?;
        final label = '${entry['idiom']} ${entry['size']}@${entry['scale']}';
        expect(name, isNotNull, reason: '$label has no image');
        final png = img.decodePng(File('$set/$name').readAsBytesSync());
        expect(png, isNotNull, reason: '$name is not a PNG');
        final pixels = (double.parse((entry['size'] as String).split('x').first) *
                int.parse((entry['scale'] as String).replaceAll('x', '')))
            .round();
        expect([png!.width, png.height], [pixels, pixels], reason: '$name is the wrong size for $label');
        // App Store Connect refuses an app icon with an alpha channel (ITMS-90717)
        // at the first upload — long after anyone could see this. Regenerate with
        // `dart run tools/make_launcher_icons.dart`, which writes them without one.
        expect(png.numChannels, 3, reason: '$name has an alpha channel');
      }
    });

    test('opens on the splash screen\'s own colour, with nothing drawn on it', () {
      // What iOS shows before Flutter's first frame. The same colour as the
      // splash, so the hand-over cannot be seen; and nothing drawn, because a
      // logo here would have to land pixel-exact on the Flutter wordmark or be
      // seen to jump — and nobody here can look at a device to check it.
      final storyboard = File('ios/Runner/Base.lproj/LaunchScreen.storyboard').readAsStringSync();
      final colour = RegExp(r'<color key="backgroundColor"([^>]*)/>').firstMatch(storyboard)?.group(1);
      expect(colour, isNotNull, reason: 'the launch screen has no background colour');
      int channel(String name) =>
          (double.parse(RegExp('$name="([0-9.]+)"').firstMatch(colour!)!.group(1)!) * 255).round();
      final surface = KhadraColors.surface.toARGB32();
      expect([channel('red'), channel('green'), channel('blue'), channel('alpha')],
          [(surface >> 16) & 0xFF, (surface >> 8) & 0xFF, surface & 0xFF, (surface >> 24) & 0xFF],
          reason: 'the launch screen is not KhadraColors.surface, which SplashScreen paints');
      expect(storyboard, isNot(contains('<imageView')));
      expect(storyboard, isNot(contains('<image ')));
    });
  });
}

/// The flavors Android builds, each with its applicationIdSuffix ('' for none),
/// read from the Gradle script so iOS is held to the same list.
Map<String, String> _androidFlavors() {
  final gradle = File('android/app/build.gradle.kts').readAsStringSync();
  final block = _braced(gradle, gradle.indexOf('productFlavors'));
  return {
    for (final match in RegExp(r'create\("(\w+)"\)').allMatches(block))
      match.group(1)!: RegExp(r'applicationIdSuffix\s*=\s*"([^"]*)"')
              .firstMatch(_braced(block, match.end))
              ?.group(1) ??
          '',
  };
}

/// The text inside the first `{ … }` at or after [from].
String _braced(String text, int from) {
  final open = text.indexOf('{', from);
  var depth = 0;
  for (var i = open; i < text.length; i++) {
    if (text[i] == '{') depth++;
    if (text[i] == '}' && --depth == 0) return text.substring(open + 1, i);
  }
  throw FormatException('unbalanced braces after offset $from');
}

/// An xcconfig's settings. `//` starts a comment anywhere in a line, as in Xcode.
Map<String, String> _xcconfig(String path) => {
      for (final line in File(path).readAsLinesSync().map((raw) => raw.split('//').first.trim()))
        if (line.isNotEmpty && !line.startsWith('#') && line.contains('='))
          line.substring(0, line.indexOf('=')).trim(): line.substring(line.indexOf('=') + 1).trim(),
    };

/// The files an xcconfig includes, in order.
List<String> _includes(String path) => [
      for (final match in RegExp(r'^#include\??\s+"([^"]+)"', multiLine: true)
          .allMatches(File(path).readAsStringSync()))
        match.group(1)!,
    ];

/// A `.strings` file's entries. Anything that is neither an entry nor a comment is
/// a syntax error that would stop the build phase, so it fails here instead.
Map<String, String> _strings(String path) {
  final text = File(path)
      .readAsStringSync()
      .replaceAll(RegExp(r'/\*[\s\S]*?\*/'), '')
      .replaceAll(RegExp(r'^\s*//.*$', multiLine: true), '');
  final entry = RegExp(r'"((?:[^"\\]|\\.)*)"\s*=\s*"((?:[^"\\]|\\.)*)"\s*;');
  final leftover = text.replaceAll(entry, '').trim();
  if (leftover.isNotEmpty) throw FormatException('$path: cannot read "$leftover"');
  String unescape(String value) =>
      value.replaceAllMapped(RegExp(r'\\(.)'), (m) => m.group(1) == 'n' ? '\n' : m.group(1)!);
  return {
    for (final match in entry.allMatches(text)) unescape(match.group(1)!): unescape(match.group(2)!),
  };
}

String? _plistString(String plist, String key) =>
    RegExp('<key>${RegExp.escape(key)}</key>\\s*<string>([^<]*)</string>').firstMatch(plist)?.group(1);

List<String> _plistArray(String plist, String key) {
  final body = RegExp('<key>${RegExp.escape(key)}</key>\\s*<array>([\\s\\S]*?)</array>').firstMatch(plist)?.group(1);
  return [for (final match in RegExp(r'<string>([^<]*)</string>').allMatches(body ?? '')) match.group(1)!];
}

String? _androidAppName(String path) =>
    RegExp(r'<string name="app_name">([^<]*)</string>').firstMatch(File(path).readAsStringSync())?.group(1);

/// The newest iOS each native plugin declares it needs, from its Package.swift and
/// its podspec — whichever the Mac build uses, the stricter one must be satisfied.
Map<String, _Version> _pluginMinimums() {
  final dependencies = File('.flutter-plugins-dependencies');
  if (!dependencies.existsSync()) {
    throw StateError('.flutter-plugins-dependencies is missing — run `flutter pub get` first');
  }
  final plugins = (((jsonDecode(dependencies.readAsStringSync()) as Map)['plugins'] as Map)['ios'] as List)
      .cast<Map<String, dynamic>>();
  final swift = [
    RegExp(r'\.iOS\(\s*"(\d+(?:\.\d+)*)"\s*\)'),
    RegExp(r'\.iOS\(\s*\.v(\d+)(?:_(\d+))?\s*\)'),
  ];
  final podspec = [
    RegExp(r'''ios\.deployment_target\s*=\s*['"](\d+(?:\.\d+)*)['"]'''),
    RegExp(r'''platform\s*=\s*:ios\s*,\s*['"](\d+(?:\.\d+)*)['"]'''),
  ];
  final minimums = <String, _Version>{};
  for (final plugin in plugins) {
    final name = plugin['name'] as String;
    var root = (plugin['path'] as String).replaceAll(r'\', '/');
    if (!root.endsWith('/')) root = '$root/';
    final found = <_Version>[];
    for (final platform in ['ios', 'darwin']) {
      for (final (file, patterns) in [
        (File('$root$platform/$name/Package.swift'), swift),
        (File('$root$platform/$name.podspec'), podspec),
      ]) {
        if (!file.existsSync()) continue;
        final source = file.readAsStringSync();
        for (final pattern in patterns) {
          for (final match in pattern.allMatches(source)) {
            found.add(_Version.parse([match.group(1), if (match.groupCount > 1) match.group(2)]
                .whereType<String>()
                .join('.')));
          }
        }
      }
    }
    if (found.isNotEmpty) minimums[name] = found.reduce((a, b) => a.compareTo(b) >= 0 ? a : b);
  }
  return minimums;
}

class _Version implements Comparable<_Version> {
  _Version(this.parts);

  factory _Version.parse(String text) => _Version([for (final part in text.split('.')) int.parse(part)]);

  final List<int> parts;

  @override
  int compareTo(_Version other) {
    for (var i = 0; i < math.max(parts.length, other.parts.length); i++) {
      final a = i < parts.length ? parts[i] : 0;
      final b = i < other.parts.length ? other.parts[i] : 0;
      if (a != b) return a.compareTo(b);
    }
    return 0;
  }

  @override
  bool operator ==(Object other) => other is _Version && compareTo(other) == 0;

  @override
  int get hashCode => Object.hashAll(parts.reversed.skipWhile((p) => p == 0));

  @override
  String toString() => parts.join('.');
}
