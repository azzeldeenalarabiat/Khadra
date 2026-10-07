import '../router.dart';

/// Khadra could not accept one of the customer's documents (Wave 4, W4-9).
///
/// A notice about the ACCOUNT, not a booking: the server sends it with no subject and no
/// reference at all, and never the reason, the document's type or anything from the file,
/// because a lock screen and an inbox are not private. My Documents, read signed in, is
/// where the customer learns which document and why, and uploads the replacement.
const documentRejectedKind = 'YourDocumentRejected';

/// Where a notification leads, from its kind and its subject. Null when it leads nowhere.
///
/// The ONE answer for both ways in: a tap on a push in any state the app was in (in front,
/// in the background, closed) and a tap on its row in Alerts. They used to decide
/// separately and both from the subject alone, so a notice that deliberately has no
/// subject — a rejected document — led nowhere from either.
///
/// - A rejected document opens My Documents, whatever else the data carries.
/// - A dispute update opens the dispute: its subject is the TICKET.
/// - Every other kind with a subject is about a booking, and opens it.
///
/// The routes are the app's own guarded routes, so a tap while signed out goes through
/// sign-in and lands where it was aimed, exactly as a typed link would.
String? notificationRoute({required String? kind, required String? subjectId}) {
  if (kind == documentRejectedKind) return Routes.documents;
  if (subjectId == null || subjectId.isEmpty) return null;
  return kind == 'YourDisputeUpdated'
      ? Routes.dispute(subjectId)
      : Routes.booking(subjectId);
}
