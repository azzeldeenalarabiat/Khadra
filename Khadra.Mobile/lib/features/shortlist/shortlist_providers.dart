import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/dtos.dart';
import '../../api/khadra_api.dart';
import '../../core/providers.dart';

/// Which cars this customer has saved, for drawing a heart.
///
/// **A set held in memory, filled from the server per page of results.** The
/// alternative — asking "is this one saved" per card — is twenty requests for one
/// screen, and holding the whole shortlist would make every catalogue page depend
/// on a list that has nothing to do with what is being searched for.
///
/// Signed out it is empty and stays empty: the catalogue is anonymous and must
/// keep working without an account, so nothing here may generate a 401 a second
/// after the app opens.
///
/// The toggle writes here FIRST and then to the server, because a heart that
/// waits for a round trip on a Jordanian mobile network reads as a tap that did
/// not land. A refusal puts it straight back — see `SavedVehiclesNotifier.toggle`.
class SavedVehiclesNotifier extends Notifier<Set<String>> {
  @override
  Set<String> build() {
    // LISTENED to, not watched.
    //
    // Watching rebuilds this notifier on every change of sign-in state, which
    // throws away the ids it has been shown — and a COLD START is exactly when
    // that happens: the catalogue renders while the token rotation is still in
    // flight, `learn` is skipped because nobody is signed in yet, and by the time
    // the session resolves nothing asks again. Every heart stayed empty until the
    // customer navigated away and back.
    ref.listen<bool>(
      sessionProvider.select((session) => session.isSignedIn),
      (_, signedIn) {
        if (!signedIn) {
          // A sign-out must not leave the previous account's hearts on screen.
          _shown.clear();
          _asked.clear();
          state = const <String>{};
          return;
        }
        // Signed in now: nothing shown while the session was resolving was ever
        // asked about.
        unawaited(learn(_shown.toList()));
      },
    );
    return const <String>{};
  }

  bool contains(String vehicleId) => state.contains(vehicleId);

  /// Every id a screen has rendered, whether or not it could be asked about yet.
  final Set<String> _shown = <String>{};

  /// The ids already answered for, so scrolling back up does not re-ask.
  final Set<String> _asked = <String>{};

  /// Learns which of these the server says are saved.
  ///
  /// Merges rather than replaces: the answer is only about the ids ASKED about,
  /// so a second page's answer must not erase the first page's hearts.
  Future<void> learn(List<String> vehicleIds) async {
    if (vehicleIds.isEmpty) return;
    // Remembered even when it cannot be asked yet, so the sign-in listener above
    // has something to catch up on.
    _shown.addAll(vehicleIds);
    if (!ref.read(sessionProvider).isSignedIn) return;

    final unknown = vehicleIds.where((id) => !_asked.contains(id)).toList();
    if (unknown.isEmpty) return;
    _asked.addAll(unknown);

    try {
      final saved = await ref.read(apiProvider).savedAmong(unknown);
      // Only the ids just asked about are corrected. Anything the customer has
      // toggled meanwhile is left alone.
      state = {
        ...state.where((id) => !unknown.contains(id)),
        ...saved,
      };
    } on Object {
      // A heart that could not be read is drawn empty. It is a decoration on a
      // catalogue page, and an error banner over the results would be worse than
      // an outline.
      _asked.removeAll(unknown);
    }
  }

  /// Takes these as saved without asking.
  ///
  /// For the SAVED LIST, where every row is saved by definition. Asking the
  /// membership endpoint about the contents of the shortlist would be asking the
  /// server to confirm what it has just said — and while nobody asked, that screen
  /// drew an empty heart on every car it was showing precisely because it was
  /// saved.
  void markSaved(Iterable<String> vehicleIds) {
    final ids = vehicleIds.toList();
    if (ids.isEmpty) return;
    _shown.addAll(ids);
    _asked.addAll(ids);
    state = {...state, ...ids};
  }

  /// Saves or forgets, from what the HEART currently shows.
  ///
  /// Only for a control whose own state came from this set. The saved-list screen
  /// must use [forget] instead: it renders rows this set may never have been
  /// asked about — a car that is no longer listed is never on a catalogue page —
  /// and a toggle there would read "not saved" and SAVE it back.
  Future<Object?> toggle(String vehicleId) =>
      state.contains(vehicleId) ? forget(vehicleId) : save(vehicleId);

  /// Returns the failure when the server refused, so the caller can say what
  /// happened — the full-list refusal in particular carries the platform's own
  /// figure and is worth showing.
  Future<Object?> save(String vehicleId) =>
      _write(vehicleId, saved: true, call: (api) => api.saveVehicle(vehicleId));

  Future<Object?> forget(String vehicleId) =>
      _write(vehicleId, saved: false, call: (api) => api.forgetVehicle(vehicleId));

  Future<Object?> _write(
    String vehicleId, {
    required bool saved,
    required Future<void> Function(KhadraApi api) call,
  }) async {
    final before = state.contains(vehicleId);
    state = saved ? {...state, vehicleId} : ({...state}..remove(vehicleId));
    // Whatever happens next, this id's answer is now known without asking again.
    _asked.add(vehicleId);

    try {
      await call(ref.read(apiProvider));
      // The saved LIST is now stale whichever way this went.
      ref.invalidate(shortlistProvider);
      return null;
    } on Object catch (failure) {
      // Straight back to what it was. A heart left filled over a save the server
      // refused is the app telling the customer something that is not true.
      state = before ? {...state, vehicleId} : ({...state}..remove(vehicleId));
      return failure;
    }
  }
}

final savedVehiclesProvider =
    NotifierProvider<SavedVehiclesNotifier, Set<String>>(
  SavedVehiclesNotifier.new,
);

/// The saved list in full, for the screen that shows it.
///
/// Separate from the membership set because it is a different question: this one
/// carries the cars, including the ones that are no longer listed, which the
/// membership set has no way to express.
final shortlistProvider =
    FutureProvider.autoDispose<List<SavedVehicle>>((ref) async {
  final session = ref.watch(sessionProvider);
  if (!session.isSignedIn) return const <SavedVehicle>[];
  return ref.watch(apiProvider).shortlist();
});

/// Whether one car is saved, without rebuilding every card when another changes.
final isSavedProvider = Provider.family<bool, String>(
  (ref, vehicleId) =>
      ref.watch(savedVehiclesProvider.select((saved) => saved.contains(vehicleId))),
);

