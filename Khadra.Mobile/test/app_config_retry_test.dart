import 'package:fake_async/fake_async.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:khadra_mobile/api/dtos.dart';
import 'package:khadra_mobile/core/providers.dart';

import 'support/fake_api.dart';

/// `/app-config` is read once and held — but a FAILED read must not be held.
///
/// Found on the staging build: the first launch met a cold Render instance, the read timed out,
/// and the error sat in the provider until the app was killed. The payments mode, the prices and
/// the date picker's bounds all went without their configuration for the whole session.
class _FlakyConfigApi extends FakeApi {
  int calls = 0;
  int failuresLeft;

  _FlakyConfigApi(this.failuresLeft);

  @override
  Future<AppConfig> appConfig() async {
    calls++;
    if (failuresLeft > 0) {
      failuresLeft--;
      throw StateError('cold server');
    }
    return FakeApi.fakeConfig(paymentsMode: 'Sandbox');
  }
}

void main() {
  test('a failed read is asked again, and the answer arrives without a restart', () {
    fakeAsync((async) {
      final api = _FlakyConfigApi(2);
      final container = ProviderContainer(overrides: [apiProvider.overrideWithValue(api)]);
      addTearDown(container.dispose);
      container.listen(appConfigProvider, (_, _) {}, fireImmediately: true);

      async.flushMicrotasks();
      expect(container.read(appConfigProvider).hasError, isTrue);
      expect(api.calls, 1);

      async.elapse(appConfigRetry);
      async.flushMicrotasks();
      expect(api.calls, 2);

      async.elapse(appConfigRetry);
      async.flushMicrotasks();
      expect(api.calls, 3);
      expect(container.read(appConfigProvider).value!.payments.isSandbox, isTrue);
    });
  });

  test('a success is read exactly once', () {
    fakeAsync((async) {
      final api = _FlakyConfigApi(0);
      final container = ProviderContainer(overrides: [apiProvider.overrideWithValue(api)]);
      addTearDown(container.dispose);
      container.listen(appConfigProvider, (_, _) {}, fireImmediately: true);

      async.flushMicrotasks();
      async.elapse(appConfigRetry * 10);
      async.flushMicrotasks();
      expect(api.calls, 1);
    });
  });
}
