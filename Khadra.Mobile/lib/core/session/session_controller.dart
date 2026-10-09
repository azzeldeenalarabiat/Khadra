import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/dtos.dart';
import '../../api/khadra_api.dart';
import '../api/api_failure.dart';
import '../uploads/document_viewer.dart';
import 'session_store.dart';

/// Who is using the app, if anyone.
///
/// Three states, and the third is the one that matters: [SessionStatus.unknown]
/// holds while the cold-start refresh is in flight. Collapsing it into
/// "signed out" would flash the sign-in screen at somebody who is signed in, every
/// single launch.
enum SessionStatus { unknown, signedOut, signedIn }

class SessionState {
  const SessionState({required this.status, this.user, this.endedReason});

  const SessionState.unknown()
      : status = SessionStatus.unknown,
        user = null,
        endedReason = null;

  final SessionStatus status;
  final AuthUser? user;

  /// Why the session ended, when it ended on its own rather than by a tap. The
  /// sign-in screen shows it, so somebody thrown out mid-task knows why.
  final SessionEndReason? endedReason;

  bool get isSignedIn => status == SessionStatus.signedIn && user != null;
  bool get isResolved => status != SessionStatus.unknown;

  /// Whether this account may actually book, as opposed to browse.
  ///
  /// Read from `/auth/me`, never inferred from a failed request: a button that
  /// finds out it is disabled by being refused has already wasted the tap.
  bool get canBook => isSignedIn && user!.isCustomer && user!.isEmailVerified;
}

enum SessionEndReason { expired, suspended, signedOutElsewhere }

/// Owns the tokens and the identity behind them.
///
/// The rules it keeps, each of which exists because breaking it signs somebody out
/// who should not be:
///
/// - **Only a definite answer ends a session.** A 401 or 403 from the refresh
///   endpoint is a verdict; a timeout, a 429 or a 5xx is a network event, and the
///   stored token survives it untouched.
/// - **The refresh token is written to disk BEFORE the access token is installed.**
///   If the process dies in between, the token on disk is the one the server just
///   consumed, and only the server's 60-second reuse grace saves the session.
/// - **Nothing is ever read out of the JWT.** Identity comes from the `UserDto` the
///   server sends, so a profile edit shows immediately rather than at the next
///   rotation.
class SessionController extends StateNotifier<SessionState> {
  SessionController({
    required KhadraApi api,
    required SessionStore store,
    Future<void> Function()? beforeSignOut,
  })
      // Private fields, public parameters. See AuthInterceptor for the same choice.
      // ignore: prefer_initializing_formals
      : _api = api,
        // ignore: prefer_initializing_formals
        _store = store,
        // ignore: prefer_initializing_formals
        _beforeSignOut = beforeSignOut,
        super(const SessionState.unknown());

  final KhadraApi _api;
  final SessionStore _store;

  /// Runs while the session is still valid, before the logout call: the push
  /// registration for this phone is removed with the credentials that own it.
  final Future<void> Function()? _beforeSignOut;

  /// Runs once at launch, before the first screen decides what to show.
  ///
  /// It must ALWAYS resolve the session, whatever happens. The router holds on the
  /// splash screen while the status is [SessionStatus.unknown], so anything that
  /// escaped from here would strand the app there for ever with no way out -- and
  /// secure storage genuinely can throw: a keystore that has not been unlocked, a
  /// browser with site data blocked, an entry written by a previous install.
  ///
  /// If the tokens cannot be read, there is no session. That is the honest answer,
  /// and it leaves the customer looking at a sign-in screen rather than a spinner.
  Future<void> restore() async {
    try {
      await _restore();
    } on Object {
      await _safeClear();
      state = const SessionState(status: SessionStatus.signedOut);
    }
  }

  Future<void> _safeClear() async {
    _epoch++;
    try {
      await _store.clear();
    } on Object {
      // If clearing fails too, there is nothing further to try.
    }
  }

  Future<void> _restore() async {
    await _store.discardDisownedTokens();

    final stored = await _store.readRefreshTokenOutcome();
    if (stored.token == null) {
      // The claim can outlive the token -- see `SessionStore.disownSession`. Drop
      // it so the marker and the store agree, rather than promising a session on
      // every launch that nothing can deliver.
      //
      // Only on a DEFINITE absence. A store that timed out or threw has said
      // nothing about whether a token is there, and treating that as "gone" would
      // discard a valid session over one slow answer -- permanently, because the
      // next launch deletes what it no longer owns. A locked keystore is exactly
      // this, and it unlocks a moment later. Same rule as `refresh` below: only a
      // verdict ends a session.
      if (stored.answered) await _store.disownSession();
      state = const SessionState(status: SessionStatus.signedOut);
      return;
    }

    // A refresh token whose own deadline has passed cannot be rotated, so asking
    // would spend a request to be told what the stored date already says.
    final expiry = await _store.readRefreshExpiry();
    if (expiry != null && DateTime.now().toUtc().isAfter(expiry)) {
      await _store.clear();
      state = const SessionState(
        status: SessionStatus.signedOut,
        endedReason: SessionEndReason.expired,
      );
      return;
    }

    final refreshed = await refresh();
    if (!refreshed && state.status == SessionStatus.unknown) {
      // The refresh failed for a reason that is NOT a verdict — offline, most
      // likely. The token stays; the app shows signed-out for now and will find
      // the session again on the next launch with a network.
      state = const SessionState(status: SessionStatus.signedOut);
    }
  }

  /// The rotation in flight, which every caller of [refresh] joins.
  Completer<bool>? _rotation;

  /// One rotation. True when a fresh access token is installed.
  ///
  /// **Single flight, here** (pre-launch item 95). Two presentations of one refresh
  /// token are the replay the server revokes a family for, and the token has more
  /// than one presenter: the cold-start [restore], and `AuthInterceptor`'s proactive
  /// and reactive paths. The interceptor's own gate covered only its callers, so a
  /// screen that asked for something while the launch rotation was still on its way
  /// started a second one. Every caller now waits on the same answer, and an
  /// exception reaches every one of them, as it reached the single caller before.
  Future<bool> refresh() {
    final running = _rotation;
    if (running != null) return running.future;

    final rotation = Completer<bool>();
    _rotation = rotation;
    unawaited(() async {
      try {
        final installed = await _rotate();
        _rotation = null;
        rotation.complete(installed);
      } catch (error, stack) {
        _rotation = null;
        rotation.completeError(error, stack);
      }
    }());
    return rotation.future;
  }

  Future<bool> _rotate() async {
    // The session this rotation is FOR. Its answer can take forty seconds and more
    // (a connect and a receive timeout, twice with the retry below), and in that time
    // the customer can sign out, sign in as somebody else, or change their password.
    // An answer for a session that has since ended or been replaced is applied to
    // nothing: installed, it signed a customer back in moments after they signed out,
    // and as a verdict, it ended the session a password change had just begun.
    final epoch = _epoch;

    // Only a store that ANSWERED "no token" ends the session. One that timed out or
    // threw has said nothing either way, and ending here would discard a valid
    // session over a keystore that unlocks a moment later — the rule `_restore`
    // already keeps.
    final stored = await _store.readRefreshTokenOutcome();
    if (epoch != _epoch) return false;
    final token = stored.token;
    if (token == null) {
      if (stored.answered) await _end(SessionEndReason.expired);
      return false;
    }

    try {
      final tokens = await _present(token);
      if (epoch != _epoch) return false;
      return await _install(tokens, epoch: epoch);
    } on ApiFailure catch (failure) {
      if (epoch != _epoch) return false;

      // Transport trouble says nothing about whether the session is valid. Ending
      // it here would sign people out every time they went through a tunnel.
      //
      // Nor does an update being required: the server refused this BUILD, not
      // these credentials. The token stays exactly where it is, the update screen
      // goes up, and the customer opens the new build still signed in. Ending the
      // session here is what a 1.0.0 build does, and the one thing about that build
      // worth not repeating.
      if (failure.isTransport ||
          failure.kind == ApiFailureKind.rateLimited ||
          failure.isUpdateRequired) {
        return false;
      }

      await _end(
        failure.hasCode('auth.account_suspended')
            ? SessionEndReason.suspended
            : SessionEndReason.expired,
      );
      return false;
    }
  }

  /// Presents [token], and once more AT ONCE when the first attempt timed out
  /// (pre-launch item 128).
  ///
  /// A timeout is the one failure after which the server may well have rotated
  /// without the phone hearing: its answer, carrying the only copy of the new
  /// token, was lost on the way back. The token on the phone is then consumed, and
  /// the next presentation — minutes later, at the next stale access token — is
  /// replay outside the sixty-second grace, and the end of the session. Presented
  /// again straight away, inside this rotation's gate, it is certainly inside the
  /// grace, and the server answers an in-grace presentation with a NEW pair and
  /// retires the one the lost answer carried.
  ///
  /// Never after anything else. A 401 is a verdict, and presenting again is exactly
  /// the replay that revokes the family. A 5xx, a 429 or a dropped connection is
  /// left to the next rotation, as before. Nor after a timeout spent CONNECTING:
  /// that request never reached the server, so nothing was consumed, and a second
  /// wait would only hold every request queued behind this rotation as long again.
  Future<AuthTokens> _present(String token) async {
    try {
      return await _api.refresh(token);
    } on ApiFailure catch (failure) {
      if (failure.kind != ApiFailureKind.timeout || failure.neverConnected) rethrow;
      return _api.refresh(token);
    }
  }

  /// Which session this controller is on. Moved on by everything that begins a
  /// session anew or ends one, so a rotation already on its way can tell its answer
  /// belongs to a session that no longer exists. See [_rotate].
  int _epoch = 0;

  Future<AuthUser> signIn(String email, String password) async {
    _epoch++;
    final tokens = await _api.signIn(email, password);
    await _install(tokens);
    return tokens.user;
  }

  /// Installs the pair `change-password` hands back.
  ///
  /// Not optional: changing a password rotates the security stamp and revokes
  /// every other family, so the token the app is holding stops working on its very
  /// next request. Without this the customer is signed out for changing their own
  /// password.
  Future<void> adoptTokens(AuthTokens tokens) async {
    _epoch++;
    await _install(tokens);
  }

  Future<void> signOut({bool allDevices = false}) async {
    _epoch++;
    // Never allowed to stop a sign-out: the hook is best effort by contract.
    try {
      await _beforeSignOut?.call();
    } on Object {
      // See above.
    }

    final token = await _store.readRefreshToken();
    if (token != null) {
      try {
        // Best effort. A failure here means the server keeps a family alive for
        // fourteen days; refusing to sign out locally over that would leave
        // somebody signed in on a device they are trying to leave.
        await _api.signOut(token, allDevices: allDevices);
      } on ApiFailure {
        // Intentionally ignored — see above.
      }
    }

    await _store.clear();
    // Best-effort and deliberately not awaited; see _end below.
    DocumentViewer.discard();
    state = const SessionState(status: SessionStatus.signedOut);
  }

  /// Re-reads the account. Cheap, and the only way a verification or a profile
  /// edit made elsewhere reaches this session.
  Future<void> reload() async {
    if (!state.isSignedIn) return;
    try {
      final user = await _api.me();
      // The session may have ended, or become somebody else's, while the account was
      // being read. Since 1.4.0 re-reads it on every sign-in, a sign-out moments
      // later would otherwise be undone by the answer.
      if (!state.isSignedIn || state.user?.id != user.id) return;
      state = SessionState(status: SessionStatus.signedIn, user: user);
    } on ApiFailure {
      // A failure here is not a reason to throw the session away; the interceptor
      // owns that decision and has better information.
    }
  }

  void applyUser(AuthUser user) {
    if (!state.isSignedIn) return;
    state = SessionState(status: SessionStatus.signedIn, user: user);
  }

  /// Called by the interceptor when the server has definitively refused.
  Future<void> endSession() => _end(SessionEndReason.expired);

  /// True when the pair is installed. A rotation passes its [epoch]: the disk write
  /// can take seconds, and a session ended or replaced meanwhile keeps what it has.
  Future<bool> _install(AuthTokens tokens, {int? epoch}) async {
    // Disk first. See the class comment.
    await _store.saveRefreshToken(tokens.refreshToken, tokens.refreshTokenExpiresAt);
    if (epoch != null && epoch != _epoch) return false;
    _store.setAccessToken(tokens.accessToken, tokens.accessTokenExpiresAt);
    state = SessionState(status: SessionStatus.signedIn, user: tokens.user);
    return true;
  }

  Future<void> _end(SessionEndReason reason) async {
    _epoch++;
    await _store.clear();
    // A licence fetched while signed in must not still be in the cache for
    // whoever signs in next on the same phone. Clearing the token without
    // clearing what the token was used to fetch would leave the document behind.
    //
    // NOT awaited. The session has to end whether or not the cache can be
    // reached: getTemporaryDirectory goes through a platform channel, and a
    // channel that never answers would leave somebody pressing Sign out on a
    // screen that stays signed in. The call swallows its own failures.
    DocumentViewer.discard();

    // A session that has already ended cannot end again, and must not acquire a
    // REASON for it on the way past. The clearing above still runs — arriving here
    // twice is not a reason to leave a token behind — but the state does not move.
    //
    // Signing out clears the tokens, and every authenticated request already in
    // flight then comes back 401: the alerts badge polls on a one-minute loop and
    // the bookings list is usually mid-fetch. `AuthInterceptor.onError` finds no
    // refresh token, calls `endSession`, and this line used to overwrite a clean
    // sign-out with `expired` — so the customer who had just tapped Sign out was
    // told on the next screen that their session had run out. A guest who touched
    // an authenticated endpoint was accused of the same thing, having never had a
    // session at all.
    //
    // `unknown` still ends, so the cold-start path is untouched.
    if (state.status == SessionStatus.signedOut) return;

    state = SessionState(status: SessionStatus.signedOut, endedReason: reason);
  }
}
