import 'package:flutter/foundation.dart';
import 'package:flutter/services.dart';

/// The licences of the fonts the app is set in (pre-launch item 196): both families are under the SIL Open
/// Font License 1.1, which lets them be bundled as long as each copy carries the licence. The texts are copied
/// unchanged from each family's official repository into `assets/fonts` and bundled as assets; this adds them
/// to Flutter's licence registry beside the packages' own, where any licence page reads them.
/// `assets/fonts/FONTS.md` lists every font file with its version, its own copyright notice and its hash.
abstract final class FontLicences {
  /// Each family, and the licence text bundled for it.
  static const Map<String, String> assets = {
    'Manrope': 'assets/fonts/OFL-Manrope.txt',
    'Noto Kufi Arabic': 'assets/fonts/OFL-NotoKufiArabic.txt',
  };
}

/// Adds the fonts' licences to [LicenseRegistry]. Read lazily — only when something asks for licences — so
/// starting the app loads nothing.
void registerFontLicences() {
  LicenseRegistry.addLicense(() async* {
    for (final MapEntry(key: family, value: asset) in FontLicences.assets.entries) {
      yield LicenseEntryWithLineBreaks(<String>[family], await rootBundle.loadString(asset));
    }
  });
}

/// For tests: the entries the registry holds for the fonts.
@visibleForTesting
Future<List<LicenseEntry>> fontLicenceEntries() async => [
      await for (final entry in LicenseRegistry.licenses)
        if (entry.packages.any(FontLicences.assets.containsKey)) entry,
    ];
