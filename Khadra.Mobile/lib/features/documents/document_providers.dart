import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/dtos.dart';
import '../../core/providers.dart';
import '../../core/uploads/document_picker.dart';

/// The document types spec 5.1 asks a renter for.
///
/// Which identity document depends on the account: a Jordanian files a national
/// ID, a foreign renter a passport. That choice was made at registration, and the
/// API's `missing` list is the authority on what is still wanted — this is only
/// what the app shows a tile for.
abstract final class DocumentTypes {
  /// A licence is TWO documents, front and back. The platform asks for both, and
  /// an app that offered one tile called "driving licence" would leave a customer
  /// unable to book with no way to see why.
  static const drivingLicenceFront = 'DrivingLicenceFront';
  static const drivingLicenceBack = 'DrivingLicenceBack';
  static const nationalId = 'NationalId';
  static const passport = 'Passport';
}

/// The caller's documents, and the checklist that says whether they may book.
///
/// Read BEFORE a booking button is enabled rather than discovered by being
/// refused: `isComplete` and `missing` exist precisely so a client can tell
/// somebody what is wanted before they try.
final myDocumentsProvider =
    FutureProvider.autoDispose<CustomerDocuments>((ref) async {
  final session = ref.watch(sessionProvider);
  if (!session.isSignedIn) {
    return const CustomerDocuments(documents: [], isComplete: false, missing: []);
  }
  return ref.watch(apiProvider).myDocuments();
});

/// How the customer chooses the file to upload: the real [DocumentPicker] (camera, photos
/// or files, checked against `/app-config`).
///
/// A provider only so a test can answer the choice without a camera or a file system, and
/// so walk the whole path from a rejection notice to the replacement being sent.
final documentChooserProvider = Provider<
    Future<DocumentChoice?> Function(BuildContext context, DocumentLimits? limits)>(
  (ref) => (context, limits) => DocumentPicker(limits).pick(context),
);
