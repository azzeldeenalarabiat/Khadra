import 'dart:typed_data';

import 'package:flutter_test/flutter_test.dart';
import 'package:khadra_mobile/api/dtos.dart';
import 'package:khadra_mobile/core/uploads/document_picker.dart';
import 'package:khadra_mobile/l10n/app_localizations_ar.dart';
import 'package:khadra_mobile/l10n/app_localizations_en.dart';

/// What the app will and will not send, and where that answer comes from.
///
/// **Every limit in these tests is constructed as a server answer.** There is no
/// case that asserts "PDFs are allowed" as a fact about the platform — the cases
/// assert that whatever `/app-config` published is what the picker applies. A
/// platform that stopped taking PDFs tomorrow would need no change here, and the
/// app would stop offering them.
void main() {
  final en = AppLocalizationsEn();
  final ar = AppLocalizationsAr();

  /// The capabilities as `/app-config` publishes them today.
  DocumentLimits published({
    List<String>? types,
    int maximumSizeBytes = 8 * 1024 * 1024,
  }) =>
      DocumentLimits(
        maximumSizeBytes,
        types ?? const ['image/jpeg', 'image/png', 'image/webp', 'application/pdf'],
      );

  Uint8List bytes(int length) => Uint8List(length)..fillRange(0, length, 7);

  group('what the server accepts', () {
    test('a PDF is sent when the server lists it', () {
      final choice = DocumentPicker(published()).check(
        en,
        bytes: bytes(2048),
        fileName: 'passport.pdf',
        contentType: 'application/pdf',
      );

      final chosen = choice as DocumentChosen;
      expect(chosen.document.contentType, 'application/pdf');
      expect(chosen.document.fileName, 'passport.pdf');
    });

    test('the same PDF is refused when the server does not list it', () {
      final choice = DocumentPicker(published(types: const ['image/jpeg'])).check(
        en,
        bytes: bytes(2048),
        fileName: 'passport.pdf',
        contentType: 'application/pdf',
      );

      expect(choice, isA<DocumentRefused>());
      // And the refusal names what IS accepted, from the same list.
      expect((choice as DocumentRefused).message, contains('JPG'));
      expect(choice.message, isNot(contains('PDF')));
    });

    test('a type the server lists in another case is still accepted', () {
      final choice = DocumentPicker(published(types: const ['IMAGE/JPEG'])).check(
        en,
        bytes: bytes(10),
        fileName: 'licence.jpg',
        contentType: 'image/jpeg',
      );

      expect(choice, isA<DocumentChosen>());
    });

    test('a file whose kind could not be established is refused, not guessed', () {
      final choice = DocumentPicker(published()).check(
        en,
        bytes: bytes(10),
        fileName: 'scan',
        contentType: null,
      );

      expect(choice, isA<DocumentRefused>());
    });

    test('a type the platform adds later needs no release', () {
      // The app holds no list of its own, so a server that starts accepting TIFF
      // starts accepting it here too.
      final choice = DocumentPicker(published(types: const ['image/tiff'])).check(
        en,
        bytes: bytes(10),
        fileName: 'licence.tiff',
        contentType: 'image/tiff',
      );

      expect(choice, isA<DocumentChosen>());
    });

    test('nothing is refused locally before /app-config has arrived', () {
      // Null limits mean the app does not yet know the rules. Refusing on a guess
      // would be the app inventing a policy; the server still applies the real one.
      final choice = const DocumentPicker(null).check(
        en,
        bytes: bytes(64 * 1024 * 1024),
        fileName: 'huge.pdf',
        contentType: 'application/pdf',
      );

      expect(choice, isA<DocumentChosen>());
    });
  });

  group('the size cap', () {
    test('one byte over the published cap is refused', () {
      final choice = DocumentPicker(published(maximumSizeBytes: 1024)).check(
        en,
        bytes: bytes(1025),
        fileName: 'scan.pdf',
        contentType: 'application/pdf',
      );

      expect(choice, isA<DocumentRefused>());
      // The figure in the sentence is the server's, converted for reading.
      expect((choice as DocumentRefused).message, contains('1 KB'));
    });

    test('exactly the cap is accepted', () {
      final choice = DocumentPicker(published(maximumSizeBytes: 1024)).check(
        en,
        bytes: bytes(1024),
        fileName: 'scan.pdf',
        contentType: 'application/pdf',
      );

      expect(choice, isA<DocumentChosen>());
    });

    test('an empty file says so rather than failing at the far end', () {
      final choice = DocumentPicker(published()).check(
        en,
        bytes: bytes(0),
        fileName: 'scan.pdf',
        contentType: 'application/pdf',
      );

      expect(choice, isA<DocumentRefused>());
      expect((choice as DocumentRefused).message, isNot(contains('8 MB')));
    });

    test('the refusal is in the reader’s language', () {
      final choice = DocumentPicker(published(maximumSizeBytes: 1024)).check(
        ar,
        bytes: bytes(2048),
        fileName: 'scan.pdf',
        contentType: 'application/pdf',
      );

      expect((choice as DocumentRefused).message, contains('أكبر من'));
    });
  });

  group('how the limits read', () {
    test('the accepted kinds are named from the server list, in order', () {
      expect(DocumentPicker(published()).acceptedKinds(), 'JPG · PNG · WEBP · PDF');
    });

    test('a type this build cannot name is shown as the platform wrote it', () {
      expect(
        DocumentPicker(published(types: const ['application/x-khadra'])).acceptedKinds(),
        'application/x-khadra',
      );
    });

    test('the cap is rendered in the unit somebody reads', () {
      expect(DocumentPicker(published()).sizeLimit(), '8 MB');
      expect(DocumentPicker(published(maximumSizeBytes: 5 * 1024 * 1024)).sizeLimit(), '5 MB');
    });

    test('a licence photograph is kilobytes, not "0 MB"', () {
      expect(DocumentPicker.formatBytes(412 * 1024), '412 KB');
      expect(DocumentPicker.formatBytes(1536 * 1024), '1.5 MB');
    });

    test('a stored content type reads as a kind', () {
      expect(DocumentPicker.kindOf('application/pdf'), 'PDF');
      expect(DocumentPicker.kindOf('image/jpeg'), 'JPG');
    });
  });
}
