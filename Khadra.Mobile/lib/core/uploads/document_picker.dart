import 'dart:typed_data';

import 'package:file_picker/file_picker.dart';
import 'package:flutter/material.dart';
import 'package:image_picker/image_picker.dart';
import 'package:mime/mime.dart';

import '../../api/dtos.dart';
import '../../l10n/app_localizations.dart';

/// Choosing a file to send the platform, in the shapes the SERVER says it takes.
///
/// **The accepted types and the size cap are `/app-config`'s, never this file's.**
/// `documents.allowedContentTypes` and `documents.maximumSizeBytes` come down with
/// every other platform fact, and the same values are what the handler checks
/// ([UploadCustomerDocumentHandler]). What lives here is the mechanics of picking
/// — which sheet entries to offer, what a chosen file actually is, and saying no
/// early enough that nobody uploads eight megabytes on a mobile network to be
/// refused at the far end.
///
/// This exists because the app could only take photographs. Both upload paths
/// opened `image_picker` and then declared `image/jpeg` regardless of what came
/// back, while `/app-config` had been advertising `application/pdf` since the
/// document store was configured. A passport scan emailed as a PDF — which is how
/// most people have one — could not be filed at all.
///
/// What a file IS is read from its leading bytes, not from its name. A `.pdf` that
/// is really a JPEG gets uploaded as what it is, and the server's answer is the
/// same either way; the point is that the app never claims something it has not
/// checked.
class DocumentPicker {
  const DocumentPicker(this.limits);

  /// The server's published capabilities. Null when `/app-config` has not arrived,
  /// in which case nothing is refused here and the server does the whole job.
  final DocumentLimits? limits;

  /// The camera writes JPEG, so it is only worth offering when JPEG is accepted.
  bool get _acceptsCameraPhotos => _accepts('image/jpeg');

  /// Whether the platform takes anything that is not a photograph — today, a PDF.
  ///
  /// Asked as "not an image" rather than "is application/pdf" so that a type added
  /// to the platform tomorrow reaches this sheet without a release.
  bool get _acceptsFiles =>
      _allowed.any((type) => !type.startsWith('image/'));

  List<String> get _allowed =>
      limits?.allowedContentTypes ?? const <String>[];

  bool _accepts(String contentType) =>
      _allowed.isEmpty ||
      _allowed.any(
          (allowed) => allowed.toLowerCase() == contentType.toLowerCase());

  /// Asks the customer where the file is coming from, then reads it.
  ///
  /// Returns null when they backed out — which is not a failure and must not
  /// produce a message.
  Future<DocumentChoice?> pick(BuildContext context) async {
    final l10n = AppLocalizations.of(context);

    final source = await showModalBottomSheet<_Source>(
      context: context,
      builder: (sheetContext) => SafeArea(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            if (_acceptsCameraPhotos)
              ListTile(
                leading: const Icon(Icons.photo_camera_outlined),
                title: Text(l10n.documentsTakePhoto),
                subtitle: Text(
                  l10n.documentsCameraNote,
                  style: const TextStyle(fontSize: 12),
                ),
                onTap: () => Navigator.of(sheetContext).pop(_Source.camera),
              ),
            ListTile(
              leading: const Icon(Icons.photo_library_outlined),
              title: Text(l10n.documentsChoosePhoto),
              onTap: () => Navigator.of(sheetContext).pop(_Source.photos),
            ),
            if (_acceptsFiles)
              ListTile(
                leading: const Icon(Icons.description_outlined),
                title: Text(l10n.documentsChooseFile),
                // The kinds and the cap are read off the server's answer, so this
                // line cannot promise something the upload would then refuse.
                subtitle: Text(
                  l10n.documentsFileLimits(acceptedKinds(), sizeLimit()),
                  style: const TextStyle(fontSize: 12),
                ),
                onTap: () => Navigator.of(sheetContext).pop(_Source.files),
              ),
          ],
        ),
      ),
    );

    if (source == null) return null;

    final picked = switch (source) {
      _Source.camera => await _photograph(ImageSource.camera),
      _Source.photos => await _photograph(ImageSource.gallery),
      _Source.files => await _file(),
    };

    if (picked == null) return null;
    return check(
      l10n,
      bytes: picked.bytes,
      fileName: picked.name,
      contentType: picked.contentType,
    );
  }

  /// A photograph, requested as JPEG on purpose.
  ///
  /// An iPhone's camera writes HEIC, which the platform does not accept, so
  /// `image_picker` is asked to hand back a JPEG and the name is corrected to
  /// match. Downscaling is for the customer's data allowance, not a rule: the
  /// server's cap is checked against the bytes that actually result.
  Future<_Picked?> _photograph(ImageSource source) async {
    final picked = await ImagePicker().pickImage(
      source: source,
      imageQuality: 88,
      maxWidth: 2400,
      requestFullMetadata: false,
    );
    if (picked == null) return null;

    final bytes = await picked.readAsBytes();
    final name = picked.name.toLowerCase().endsWith('.jpg') ||
            picked.name.toLowerCase().endsWith('.jpeg')
        ? picked.name
        : '${picked.name}.jpg';

    return _Picked(bytes, name, 'image/jpeg');
  }

  /// Anything else the platform takes, which today means a PDF.
  ///
  /// The native picker is narrowed to the server's own types where the platform
  /// can express them as extensions; where it cannot, everything is offered and
  /// the refusal below does the work. Either way the check is the same one.
  Future<_Picked?> _file() async {
    final extensions = _allowedExtensions();

    final result = await FilePicker.pickFiles(
      type: extensions.isEmpty ? FileType.any : FileType.custom,
      allowedExtensions: extensions.isEmpty ? null : extensions,
      withData: true,
      allowMultiple: false,
    );

    final files = result?.files ?? const <PlatformFile>[];
    final file = files.length == 1 ? files.first : null;
    if (file == null) return null;

    final bytes = file.bytes;
    if (bytes == null) return _Picked(Uint8List(0), file.name, null);

    // The NAME is a hint and the bytes are the evidence. `lookupMimeType` reads
    // the magic numbers when it is given them and falls back to the extension
    // when the bytes say nothing it recognises.
    final contentType = lookupMimeType(
      file.name,
      headerBytes: bytes.take(defaultMagicNumbersMaxLength).toList(),
    );

    return _Picked(bytes, file.name, contentType);
  }

  /// The server's own answer, reached before anything leaves the phone.
  ///
  /// Public because it is the whole of this class worth testing: the sheet and
  /// the native pickers cannot run in a unit test, and this is where the
  /// published capabilities are actually applied.
  DocumentChoice check(
    AppLocalizations l10n, {
    required Uint8List bytes,
    required String fileName,
    required String? contentType,
  }) {
    if (bytes.isEmpty) {
      return DocumentRefused(l10n.documentsFileUnreadable);
    }

    final cap = limits?.maximumSizeBytes;
    if (cap != null && bytes.length > cap) {
      return DocumentRefused(l10n.documentsTooLarge(sizeLimit()));
    }

    if (contentType == null || !_accepts(contentType)) {
      return DocumentRefused(l10n.documentsWrongType(acceptedKinds()));
    }

    return DocumentChosen(PickedDocument(
      bytes: bytes,
      fileName: fileName,
      contentType: contentType,
    ));
  }

  /// The accepted types as a reader would name them: "JPG, PNG, WebP, PDF".
  ///
  /// Rendered FROM the server's list rather than written down beside it, so a type
  /// the platform adds appears here by itself. A type this build cannot name a
  /// file extension for is shown as the platform's own word for it.
  String acceptedKinds() {
    final kinds = <String>[];
    for (final type in _allowed) {
      final extension = extensionFromMime(type)?.toUpperCase() ?? type;
      if (!kinds.contains(extension)) kinds.add(extension);
    }
    return kinds.join(' · ');
  }

  /// The cap, in the unit somebody reads it in.
  String sizeLimit() => formatBytes(limits?.maximumSizeBytes ?? 0);

  List<String> _allowedExtensions() {
    final extensions = <String>[];
    for (final type in _allowed) {
      final extension = extensionFromMime(type);
      if (extension != null && !extensions.contains(extension)) {
        extensions.add(extension);
      }
    }
    return extensions;
  }

  /// A byte count as a file manager would show it.
  ///
  /// Kilobytes below a megabyte, because a licence photograph is often 400 KB and
  /// "0 MB" reads as an empty file.
  static String formatBytes(int bytes) {
    const kilobyte = 1024;
    const megabyte = kilobyte * 1024;
    if (bytes >= megabyte) {
      final megabytes = bytes / megabyte;
      // A round figure keeps no decimal. The published cap is 8 MB, and "8.0 MB"
      // reads as a measurement of the limit rather than the limit itself.
      return megabytes == megabytes.roundToDouble()
          ? '${megabytes.round()} MB'
          : '${megabytes.toStringAsFixed(1)} MB';
    }
    return '${(bytes / kilobyte).ceil()} KB';
  }

  /// A content type as a reader would name it: `application/pdf` is "PDF".
  static String kindOf(String contentType) =>
      extensionFromMime(contentType)?.toUpperCase() ?? contentType;
}

/// A file the customer chose and the app is prepared to send.
class PickedDocument {
  const PickedDocument({
    required this.bytes,
    required this.fileName,
    required this.contentType,
  });

  final Uint8List bytes;
  final String fileName;

  /// What the bytes actually are, never assumed from the source of them.
  final String contentType;
}

/// The outcome of choosing a file. Backing out returns null instead of one of
/// these: a customer who changed their mind has not been refused anything.
sealed class DocumentChoice {
  const DocumentChoice();
}

final class DocumentChosen extends DocumentChoice {
  const DocumentChosen(this.document);

  final PickedDocument document;
}

final class DocumentRefused extends DocumentChoice {
  const DocumentRefused(this.message);

  /// Already in the reader's language, and already says what to do instead.
  final String message;
}

enum _Source { camera, photos, files }

class _Picked {
  const _Picked(this.bytes, this.name, this.contentType);

  final Uint8List bytes;
  final String name;
  final String? contentType;
}
