import 'package:flutter_test/flutter_test.dart';
import 'package:khadra_mobile/core/fonts/font_licences.dart';

/// The fonts' licences travel inside the app (pre-launch item 196): both families' SIL Open Font License texts
/// are bundled as assets and join the packages' licences in Flutter's registry, where any licence page reads
/// them. The API's test suite holds the texts, the font files and their notice (`assets/fonts/FONTS.md`) to one
/// another; this proves the app bundles and registers them.
void main() {
  TestWidgetsFlutterBinding.ensureInitialized();

  test('each font family is registered with its own licence text', () async {
    registerFontLicences();

    final entries = await fontLicenceEntries();
    final families = {for (final entry in entries) ...entry.packages};

    expect(families, containsAll(FontLicences.assets.keys));
    for (final entry in entries) {
      final text = entry.paragraphs.map((paragraph) => paragraph.text).join('\n');
      expect(text, contains('SIL Open Font License, Version 1.1'), reason: entry.packages.join());
      expect(text, contains(entry.packages.contains('Manrope') ? 'The Manrope Project Authors' : 'The Noto Project Authors'));
    }
  });
}
