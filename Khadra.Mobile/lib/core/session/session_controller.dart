import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/dtos.dart';
import '../../api/khadra_api.dart';
import '../api/api_failure.dart';
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
  SessionController({required KhadraApi api, required SessionStore store})
      // Private fields, public parameters. See AuthInterceptor for the same choice.
      // ignore: prefer_initializing_formals
      : _api = api,
        // ignore: prefer_initializing_formals
        _store = store,
        super(const SessionState.unknown());

  final KhadraApi _api;
  final SessionStore _store;

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
    try {
      await _store.clear();
    } on Object {
      // If clearing fails too, there is nothing further to try.
    }
  }

  Future<void> _restore() async {
    await _store.clearIfReinstalled();

    final token = await _store.readRefreshToken();
    if (token == null) {
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

  /// One rotation. True when a fresh access token is installed.
  ///
  /// Never call this concurrently — `AuthInterceptor` is what serialises it, and
  /// two rotations of one token is the replay the server revokes a family for.
  Future<bool> refresh() async {
    final token = await _store.readRefreshToken();
    if (token == null) {
      await _end(SessionEndReason.expired);
      return false;
    }

    try {
      final tokens = await _api.refresh(token);
      await _install(tokens);
      return true;
    } on ApiFailure catch (failure) {
      // Transport trouble says nothing about whether the session is valid. Ending
      // it here would sign people out every time they went through a tunnel.
      if (failure.isTransport || failure.kind == ApiFailureKind.rateLimited) {
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

  Future<AuthUser> signIn(String email, String password) async {
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
  Future<void> adoptTokens(AuthTokens tokens) => _install(tokens);

  Future<void> signOut({bool allDevices = false}) async {
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
    state = const SessionState(status: SessionStatus.signedOut);
  }

  /// Re-reads the account. Cheap, and the only way a verification or a profile
  /// edit made elsewhere reaches this session.
  Future<void> reload() async {
    if (!state.isSignedIn) return;
    try {
      state = SessionState(status: SessionStatus.signedIn, user: await _api.me());
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

  Future<void> _install(AuthTokens tokens) async {
    // Disk first. See the class comment.
    await _store.saveRefreshToken(tokens.refreshToken, tokens.refreshTokenExpiresAt);
    _store.setAccessToken(tokens.accessToken, tokens.accessTokenExpiresAt);
    state = SessionState(status: SessionStatus.signedIn, user: tokens.user);
  }

  Future<void> _end(SessionEndReason reason) async {
    await _store.clear();
    state = SessionState(status: SessionStatus.signedOut, endedReason: reason);
  }
}
