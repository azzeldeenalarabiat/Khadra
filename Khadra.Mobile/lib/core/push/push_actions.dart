import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../features/bookings/booking_providers.dart';
import '../../features/documents/document_providers.dart';
import '../live/live_refresh.dart';
import '../live/live_surfaces.dart';
import 'notification_route.dart';
import 'push_trace.dart';

/// Opens where a tapped push leads ([notificationRoute]), from any state the app was in.
///
/// **Gone to, not pushed, until the session has resolved.** The tap that launches a closed
/// app arrives before the stored session is restored, and every route a push leads to is
/// guarded, so the router parks it on the splash with the destination as `next`. Pushed,
/// that parked copy sat on top of a splash that knew no `next`: when the session answered,
/// the splash sent the customer Home and the tap was lost — for a booking as much as for a
/// rejected document. Gone to, the destination travels through the wait exactly as a deep
/// link does, and the screen opens once the session is back (or sign-in, with the
/// destination kept, when it is not).
///
/// **Pushed once it has**, so the back arrow returns to whatever the customer was looking
/// at when they tapped.
void openForPush(
  String location, {
  required GoRouter router,
  required bool sessionResolved,
}) {
  PushTrace.record('navigate', detail: '${sessionResolved ? 'push' : 'go'} ${PushTrace.redact(location)}');
  if (sessionResolved) {
    router.push(location);
  } else {
    router.go(location);
  }
  // TEMPORARY, Staging only (PushTrace): which screen is on top once the navigation has
  // had time to land, the session included.
  if (PushTrace.enabled) {
    Timer(const Duration(milliseconds: 1500), () {
      try {
        final top = router.routerDelegate.currentConfiguration.last.matchedLocation;
        PushTrace.record('screen', detail: 'top=${PushTrace.redact(top)}');
      } on Object catch (error) {
        PushTrace.record('screen', detail: 'unreadable ${error.runtimeType}');
      }
    });
  }
}

/// What a push that arrives while the app is in front refreshes, now rather than at the
/// next poll.
///
/// - The booking it is about, if it is about one (a dispute update's subject is the
///   ticket, not a booking).
/// - My Documents, for a document Khadra could not accept: a customer looking at the
///   screen sees the refusal and its reason without pulling to refresh.
/// - Every live surface, through the policy's own "something changed" trigger.
///
/// Functions of their own, these two, so the app and its tests run the same rules.
void refreshForPush(
  Map<String, String> data, {
  required void Function(ProviderOrFamily provider) invalidate,
  required LiveRefresh live,
}) {
  final subject = data['subjectId'];
  if (subject != null && data['kind'] != 'YourDisputeUpdated') {
    invalidate(bookingProvider(subject));
  }
  if (data['kind'] == documentRejectedKind) invalidate(myDocumentsProvider);
  for (final surface in Surfaces.all) {
    live.touch(surface);
  }
}
