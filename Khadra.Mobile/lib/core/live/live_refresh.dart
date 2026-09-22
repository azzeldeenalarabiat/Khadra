import 'dart:async';

import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

/// Whether the app is in front of somebody. See [LiveRefresh].
final appResumedProvider = StateProvider<bool>((ref) => true);

/// Which tab is showing, so only the surface a customer is looking at re-reads itself.
final visibleTabProvider = StateProvider<int>((ref) => 0);

/// One thing that can re-read itself.
@immutable
class LiveSurface {
  const LiveSurface({
    required this.id,
    required this.refresh,
    this.poll,
    this.visible,
  });

  /// Unique, and what [LiveRefresh.touch] and the future push channel name.
  final String id;

  /// Re-reads it. Must NOT throw: a quiet refresh that fails keeps the data it had.
  final Future<bool> Function() refresh;

  /// How often to poll while visible. Null means never.
  final Duration? poll;

  /// True while a customer is actually looking at this. Null means always.
  final bool Function()? visible;
}

/// When a surface last had a good answer, and when it is next due one.
class _State {
  /// Monotonic. Null means never successfully read — not zero, which is a real reading.
  int? lastGood;
  int nextDue = 0;
  int failures = 0;
  bool inFlight = false;
}

/// One place that decides when anything is re-read. See `docs/refresh-policy.md`.
///
/// The same policy the Angular console implements, in this app's own idiom. The two share no code,
/// so the document is what keeps them the same — change it there first.
///
/// Three rules do the work:
///
/// - **Nothing polls while the app is not `resumed`.** Not a battery nicety. Every request runs
///   `AuthInterceptor.onRequest`, which rotates the refresh token whenever the access token is
///   stale — so a backgrounded app would rotate every few minutes for as long as it sat there, and
///   each rotation is a chance to hit pre-launch items 126 and 128 and sign the customer out of a
///   session they never left.
/// - **Only the surface in front of somebody polls.** The bookings list does not re-read itself
///   while its owner is browsing cars.
/// - **A refresh nobody asked for may not take data away.** A failed poll keeps the last answer and
///   backs off. Pull-to-refresh still shows an error, because somebody is waiting for one.
class LiveRefresh {
  /// [monotonicMillis] replaces the clock, for tests only.
  ///
  /// `Stopwatch` reads real time and `fake_async` cannot reach it, so a test that elapsed an hour
  /// of fake time would find this class convinced no time had passed at all. Injecting the reading
  /// is the smallest honest way in; nothing in the app passes it.
  // The field is private and the parameter is not; `this._monotonicMillis` would put an underscore
  // in the signature. Same choice as `AuthInterceptor` and `SessionStore`.
  LiveRefresh({int Function()? monotonicMillis})
      // ignore: prefer_initializing_formals
      : _monotonicMillis = monotonicMillis;

  final int Function()? _monotonicMillis;

  /// A monotonic clock. The WALL clock is corrected at exactly the moment "resumed" fires, so a
  /// phone waking from sleep could otherwise compute a negative age and hold a refresh back.
  final Stopwatch _clock = Stopwatch()..start();

  final Map<String, LiveSurface> _surfaces = <String, LiveSurface>{};
  final Map<String, _State> _states = <String, _State>{};

  Timer? _heartbeat;
  bool _awake = true;

  /// The floor under every trigger except a write. Twenty seconds is short enough that a customer
  /// returning to a tab sees a fresh answer, and long enough that flicking between tabs is not a
  /// burst of requests.
  static const Duration floor = Duration(seconds: 20);

  /// One timer for every surface. N surfaces cost one wakeup, not N.
  static const Duration heartbeat = Duration(seconds: 5);

  static const Duration backoffCap = Duration(minutes: 10);

  int get _now => _monotonicMillis?.call() ?? _clock.elapsedMilliseconds;

  /// Takes a surface under the policy.
  ///
  /// It is counted as JUST READ, because it is: these are registered at the moment the screens that
  /// own them mount and fetch for themselves. Without that, the first trigger A — a tab switch
  /// seconds later — would find no recorded success, skip the floor and re-read a list that had only
  /// just arrived. Opening the Bookings tab cost two reads instead of one until this line existed.
  void register(LiveSurface surface) {
    _surfaces[surface.id] = surface;
    _states.putIfAbsent(surface.id, () => _State()..lastGood = _now);
    _start();
  }

  void unregister(String id) {
    _surfaces.remove(id);
    _states.remove(id);
    if (_surfaces.isEmpty) _stop();
  }

  void dispose() {
    _stop();
    _surfaces.clear();
    _states.clear();
  }

  /// The app came to the front, or a tab did. Trigger A.
  void becameVisible() {
    if (!_awake) return;
    for (final surface in _surfaces.values) {
      if (_isVisible(surface)) unawaited(_refresh(surface, force: false));
    }
  }

  /// Something changed and we know it. Trigger C — and where the push channel will arrive.
  ///
  /// [force] skips the floor, which is right for a write: a customer who cancels a booking must see
  /// it move, not be told to wait twenty seconds. A push passes false, so a chatty server still
  /// cannot make this app ask faster than the policy allows.
  void touch(String id, {bool force = true}) {
    final surface = _surfaces[id];
    if (surface != null) unawaited(_refresh(surface, force: force));
  }

  /// Follows the app's lifecycle. Anything but `resumed` stops every poll.
  void setResumed(bool resumed) {
    if (_awake == resumed) return;
    _awake = resumed;
    if (resumed) {
      _start();
      becameVisible();
    } else {
      _stop();
    }
  }

  void _start() {
    if (!_awake || _surfaces.isEmpty) return;
    _heartbeat ??= Timer.periodic(heartbeat, (_) => _tick());
  }

  void _stop() {
    _heartbeat?.cancel();
    _heartbeat = null;
  }

  /// Trigger B.
  void _tick() {
    if (!_awake) return;
    final at = _now;
    for (final surface in _surfaces.values) {
      if (surface.poll == null || !_isVisible(surface)) continue;
      if (at >= _states[surface.id]!.nextDue) unawaited(_refresh(surface, force: false));
    }
  }

  bool _isVisible(LiveSurface surface) => surface.visible?.call() ?? true;

  Future<void> _refresh(LiveSurface surface, {required bool force}) async {
    final state = _states[surface.id]!;
    // Single flight. Never restart a read that is already on its way.
    if (state.inFlight) return;

    final at = _now;
    if (!force && state.lastGood != null && at - state.lastGood! < floor.inMilliseconds) return;

    state.inFlight = true;
    try {
      if (await surface.refresh()) {
        state.lastGood = _now;
        state.failures = 0;
        state.nextDue = state.lastGood! + (surface.poll?.inMilliseconds ?? 0);
      } else {
        _backOff(surface, state);
      }
    } on Object {
      // A surface that threw rather than answering false. Same treatment: the data it had stays.
      _backOff(surface, state);
    } finally {
      state.inFlight = false;
    }
  }

  void _backOff(LiveSurface surface, _State state) {
    state.failures += 1;
    final base = surface.poll ?? floor;
    final delay = base * (1 << state.failures);
    state.nextDue =
        _now + (delay > backoffCap ? backoffCap.inMilliseconds : delay.inMilliseconds);
  }

  /// How long ago this surface last had a good answer, or null if it never has.
  Duration? ageOf(String id) {
    final lastGood = _states[id]?.lastGood;
    return lastGood == null ? null : Duration(milliseconds: _now - lastGood);
  }

  /// Ages a surface past the floor, so a test can reach "the customer came back later" without
  /// spending the twenty seconds. There is no other way in: the clock is monotonic by design, which
  /// is exactly what stops a test winding it back.
  @visibleForTesting
  void markStale(String id) {
    final state = _states[id];
    if (state != null) state.lastGood = _now - floor.inMilliseconds - 1;
  }
}

final liveRefreshProvider = Provider<LiveRefresh>((ref) {
  final live = LiveRefresh();
  ref.onDispose(live.dispose);
  return live;
});
