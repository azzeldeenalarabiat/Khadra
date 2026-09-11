import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:khadra_mobile/core/api/api_failure.dart';
import 'package:khadra_mobile/core/providers.dart';
import 'package:khadra_mobile/features/shortlist/shortlist_providers.dart';

import 'support/fake_api.dart';

/// The heart's own state.
///
/// It is OPTIMISTIC: the set changes before the server is asked, because a heart
/// that waits for a round trip on a mobile network reads as a tap that did not
/// land. Everything worth testing here follows from that — a refusal has to put
/// it back exactly, and a screen that renders rows the set was never asked about
/// must not toggle blind.
void main() {
  late _ShortlistApi api;

  /// Signed in the way the app itself does it, through the token pair the server
  /// hands back, so nothing here depends on a test-only door into the session.
  Future<ProviderContainer> containerFor({bool signedIn = true}) async {
    final container = ProviderContainer(
      overrides: [apiProvider.overrideWithValue(api)],
    );
    addTearDown(container.dispose);

    if (signedIn) {
      await container.read(sessionProvider.notifier).adoptTokens(FakeApi.fakeTokens());
    }
    return container;
  }

  setUp(() => api = _ShortlistApi());

  test('learning fills the set from what the server says', () async {
    api.saved = {'car-a'};
    final container = await containerFor();
    final notifier = container.read(savedVehiclesProvider.notifier);

    await notifier.learn(['car-a', 'car-b']);

    expect(container.read(savedVehiclesProvider), {'car-a'});
    expect(api.askedAbout, [
      ['car-a', 'car-b']
    ]);
  });

  test('a second page does not re-ask about the first', () async {
    api.saved = {'car-a'};
    final container = await containerFor();
    final notifier = container.read(savedVehiclesProvider.notifier);

    await notifier.learn(['car-a', 'car-b']);
    await notifier.learn(['car-b', 'car-c']);

    // Only the id it had not seen. Scrolling a long list must not re-ask for
    // every card already on screen.
    expect(api.askedAbout, [
      ['car-a', 'car-b'],
      ['car-c'],
    ]);
    // And the first page's answer survives the second page's.
    expect(container.read(savedVehiclesProvider), {'car-a'});
  });

  test('saving shows immediately and survives the server agreeing', () async {
    final container = await containerFor();
    final notifier = container.read(savedVehiclesProvider.notifier);

    final pending = notifier.save('car-a');
    // Before the await: the heart is already filled.
    expect(container.read(savedVehiclesProvider), contains('car-a'));

    expect(await pending, isNull);
    expect(container.read(savedVehiclesProvider), contains('car-a'));
    expect(api.savedCalls, ['car-a']);
  });

  test('a refused save puts the heart back and hands the failure over', () async {
    api.failWith = const ApiFailure(
      kind: ApiFailureKind.validation,
      code: 'shortlist.full',
    );
    final container = await containerFor();
    final notifier = container.read(savedVehiclesProvider.notifier);

    final failure = await notifier.save('car-a');

    // The app must not tell somebody a car is saved when the server said no.
    expect(container.read(savedVehiclesProvider), isNot(contains('car-a')));
    expect(failure, isA<ApiFailure>());
    expect((failure! as ApiFailure).code, 'shortlist.full');
  });

  test('a refused remove puts the heart back filled', () async {
    api.saved = {'car-a'};
    final container = await containerFor();
    final notifier = container.read(savedVehiclesProvider.notifier);
    await notifier.learn(['car-a']);

    api.failWith = const ApiFailure(kind: ApiFailureKind.offline);
    final failure = await notifier.forget('car-a');

    expect(failure, isNotNull);
    expect(container.read(savedVehiclesProvider), contains('car-a'));
  });

  /// The bug this method exists to prevent.
  ///
  /// The saved-list screen renders rows the membership set was never asked about
  /// — a car that is no longer listed never appears on a catalogue page — so a
  /// toggle there would read "not saved" and SAVE it straight back.
  test('forget removes a car the set was never asked about', () async {
    final container = await containerFor();
    final notifier = container.read(savedVehiclesProvider.notifier);

    expect(container.read(savedVehiclesProvider), isNot(contains('car-gone')));
    await notifier.forget('car-gone');

    expect(api.forgottenCalls, ['car-gone']);
    expect(api.savedCalls, isEmpty);
  });

  test('signed out, nothing is asked and nothing is held', () async {
    final container = await containerFor(signedIn: false);
    final notifier = container.read(savedVehiclesProvider.notifier);

    await notifier.learn(['car-a']);

    // The catalogue is anonymous and must keep working; a membership request a
    // second after the app opens would be a 401 for nothing.
    expect(api.askedAbout, isEmpty);
    expect(container.read(savedVehiclesProvider), isEmpty);
  });
}

class _ShortlistApi extends FakeApi {
  Set<String> saved = <String>{};
  ApiFailure? failWith;

  final List<List<String>> askedAbout = [];
  final List<String> savedCalls = [];
  final List<String> forgottenCalls = [];

  @override
  Future<Set<String>> savedAmong(List<String> vehicleIds) async {
    askedAbout.add(vehicleIds);
    return saved.intersection(vehicleIds.toSet());
  }

  @override
  Future<void> saveVehicle(String vehicleId) async {
    if (failWith != null) throw failWith!;
    savedCalls.add(vehicleId);
    saved.add(vehicleId);
  }

  @override
  Future<void> forgetVehicle(String vehicleId) async {
    if (failWith != null) throw failWith!;
    forgottenCalls.add(vehicleId);
    saved.remove(vehicleId);
  }
}
