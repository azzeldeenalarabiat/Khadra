import 'dart:io';

import 'package:flutter/foundation.dart';
import 'package:open_filex/open_filex.dart';
import 'package:path_provider/path_provider.dart';

/// Opens a document the app has already fetched, in the platform's own viewer.
///
/// The bytes arrive over an AUTHENTICATED request and are written to this app's
/// private cache, never to Downloads or anywhere else a gallery scans. A passport
/// is not a file to leave lying in shared storage, and the cache directory is
/// both private to this app and something the operating system may reclaim on its
/// own — which is the right lifetime for a copy made to satisfy one tap.
///
/// The file is overwritten per document rather than accumulating one copy per
/// view: the name comes from the document's id, so pressing View twice reuses the
/// same path instead of filling the cache with duplicates of a licence.
abstract final class DocumentViewer {
  /// The sub-directory everything written here lives in, so `discard` can empty
  /// it without guessing which cache files were ours.
  static const _folder = 'khadra_documents';

  /// Writes the bytes and asks the platform to open them.
  ///
  /// Returns false when the platform has nothing that can open the file, or
  /// refuses to. That is not an error worth a stack trace — it is a phone with no
  /// PDF reader — so the caller says so in words instead.
  static Future<bool> open({
    required Uint8List bytes,
    required String? contentType,
    required String documentId,
  }) async {
    // Nothing to write and nothing to show. Treated as a failure to open rather
    // than as success, because an empty viewer is indistinguishable from a bug.
    if (bytes.isEmpty) return false;

    // There is no cache directory on the web, and nothing to hand a file path to
    // either; the browser build keeps the old behaviour of not offering this.
    if (kIsWeb) return false;

    final file = await _write(bytes, contentType, documentId);
    final result = await OpenFilex.open(file.path, type: contentType);
    return result.type == ResultType.done;
  }

  /// Deletes everything this app has written for viewing.
  ///
  /// Called when the session ends: a document fetched while signed in must not
  /// still be sitting in the cache for whoever signs in next on the same phone.
  static Future<void> discard() async {
    if (kIsWeb) return;
    try {
      final directory = Directory(await _directoryPath());
      if (directory.existsSync()) await directory.delete(recursive: true);
    } on Object {
      // EVERYTHING, not just Exception. A cache the operating system has already
      // reclaimed, a file the viewer still holds open, a platform channel that
      // is not there at all -- none of it is worth failing a sign-out over, and
      // catching only Exception let a MissingPluginException escape and leave
      // the session signed IN. Clearing a cache is cleanup; it must never be
      // the reason somebody cannot leave.
    }
  }

  static Future<File> _write(
    Uint8List bytes,
    String? contentType,
    String documentId,
  ) async {
    final directory = Directory(await _directoryPath());
    if (!directory.existsSync()) directory.createSync(recursive: true);

    final file = File('${directory.path}/$documentId${extensionFor(contentType)}');
    await file.writeAsBytes(bytes, flush: true);
    return file;
  }

  static Future<String> _directoryPath() async =>
      '${(await getTemporaryDirectory()).path}/$_folder';

  /// The platform opens by EXTENSION as much as by type, so the name has to carry
  /// one. Derived from what the server said the bytes are, and `.bin` when it said
  /// nothing — which produces an honest "no app can open this" rather than a
  /// picture named as a PDF.
  @visibleForTesting
  static String extensionFor(String? contentType) => switch (contentType) {
        'application/pdf' => '.pdf',
        'image/jpeg' => '.jpg',
        'image/png' => '.png',
        'image/webp' => '.webp',
        _ => '.bin',
      };
}
