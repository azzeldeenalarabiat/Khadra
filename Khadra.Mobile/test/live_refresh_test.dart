import 'package:fake_async/fake_async.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:khadra_mobile/core/live/live_refresh.dart';

/// The refresh policy, counted rather than trusted. See `docs/refresh-policy.md`.
///
/// The Angular console has the same spec in its own idiom (`live-refresh.service.spec.ts`). Neither
/// can import the other, so the two suites are what keep the two clients honest about one policy.
///
/// The first test is the one that matters most, and it is not about batteries. Every request this
/// app makes runs `AuthInterceptor.onRequest`, which rotates the refresh token whenever the access
/// token is stale — so an app polling in the background rotates every few minutes for as long as it
/// sits there, and each rotation is a chance to hit pre-launch items 126 and 128 and sign a customer
/// out of a session they never left.
void main() {
  late LiveRefresh live;
  late int reads;
  late bool answers;

  LiveSurface surface({
    String id = 'queue',
    Duration? poll = const Duration(seconds: 60),
    bool Function()? visible,
  }) =>
      LiveSurface(
        id: id,
        poll: poll,
        visible: visible,
        refresh: () async {
          reads += 1;
          return answers;
        },
      );

  void run(void Function(FakeAsync async) body) {
    fakeAsync((async) {
      reads = 0;
      answers = true;
      live = LiveRefresh(monotonicMillis: () => async.elapsed.inMilliseconds);
      try {
        body(async);
      } finally {
        live.dispose();
        async.flushTimers();
      }
    });
  }

  test('an app that is not resumed makes no requests at all', () {
    run((async) {
      live.register(surface());
      live.setResumed(false);

      async.elapse(const Duration(minutes: 30));

      expect(reads, 0, reason: 'a backgrounded app must not poll, or it rotates tokens all night');

      // And it starts again the moment somebody picks the phone up.
      live.setResumed(true);
      async.flushMicrotasks();
      expect(reads, greaterThan(0));
    });
  });

  test('only the surface in front of somebody polls', () {
    run((async) {
      live.register(surface(visible: () => false));

      async.elapse(const Duration(minutes: 5));

      expect(reads, 0);
    });
  });

  test('a visible surface polls on its own cadence', () {
    run((async) {
      live.register(surface(poll: const Duration(seconds: 60)));
      async.elapse(const Duration(seconds: 65));
      expect(reads, 1);

      async.elapse(const Duration(seconds: 65));
      expect(reads, 2);
    });
  });

  test('coming back inside the floor asks for nothing', () {
    run((async) {
      live.register(surface(poll: null));

      // Registration counts as just-read: these are registered when the screens that own them
      // mount and fetch for themselves.
      live.becameVisible();
      expect(reads, 0);

      async.elapse(LiveRefresh.floor + const Duration(seconds: 1));
      live.becameVisible();
      expect(reads, 1, reason: 'but past the floor, a return re-reads — pre-launch item 125');
    });
  });

  test('a write goes through the floor immediately', () {
    run((async) {
      live.register(surface(poll: null));

      live.touch('queue');
      expect(reads, 1, reason: 'a customer who cancels must see it move, not wait twenty seconds');
    });
  });

  test('a push does NOT go through the floor', () {
    run((async) {
      live.register(surface(poll: null));

      // `force: false`. A chatty server must not be able to make this app ask faster than the
      // policy allows — the pulse says what changed, it does not set the pace.
      live.touch('queue', force: false);
      expect(reads, 0);
    });
  });

  test('a failed refresh backs off and a good one resets it', () {
    run((async) {
      live.register(surface(poll: const Duration(seconds: 60)));

      answers = false;
      async.elapse(const Duration(seconds: 65));
      expect(reads, 1);

      // One failure: 60 x 2^1 = two minutes away now, not one.
      async.elapse(const Duration(seconds: 65));
      expect(reads, 1);
      async.elapse(const Duration(seconds: 65));
      expect(reads, 2);

      // Two failures running: 60 x 2^2 = four minutes. An app against a dead API asks less and less
      // rather than hammering it.
      answers = true;
      async.elapse(const Duration(seconds: 65));
      expect(reads, 2, reason: 'still backed off');

      async.elapse(const Duration(minutes: 4));
      expect(reads, greaterThan(2), reason: 'and it does come back');

      // And the cadence is a plain minute again: exactly one more read per minute, not the four
      // the backoff had been holding at.
      final settled = reads;
      async.elapse(const Duration(seconds: 65));
      expect(reads, settled + 1, reason: 'one success and the backoff is forgotten');
    });
  });

  test('one surface, one request in flight', () {
    run((async) {
      var release = false;
      live.register(LiveSurface(
        id: 'slow',
        poll: const Duration(seconds: 60),
        refresh: () async {
          reads += 1;
          while (!release) {
            await Future<void>.delayed(const Duration(seconds: 1));
          }
          return true;
        },
      ));

      async.elapse(const Duration(minutes: 5));
      expect(reads, 1, reason: 'a reload already on its way is never restarted');

      release = true;
      async.elapse(const Duration(seconds: 5));
    });
  });
}
