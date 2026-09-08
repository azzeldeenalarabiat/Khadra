import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/dtos.dart';
import '../../core/providers.dart';

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
