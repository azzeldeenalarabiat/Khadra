import 'package:flutter_test/flutter_test.dart';
import 'package:khadra_mobile/core/api/api_failure.dart';
import 'package:khadra_mobile/core/paging.dart';

/// The paging primitive every long list in this app now shares.
///
/// The branch worth testing is the FAILURE one: when page three does not arrive,
/// the two pages already read are still perfectly good, and the mistake this
/// guards against is replacing them with an error state — which throws away what
/// the customer was reading, mid-scroll.
void main() {
  PagedList<String> page(
    List<String> items, {
    int at = 1,
    int total = 10,
    bool hasMore = true,
  }) =>
      PagedList<String>(
        items: items,
        page: at,
        total: total,
        hasMore: hasMore,
      );

  test('a page is appended to what was already read', () async {
    final emitted = <PagedList<String>>[];

    final result = await loadNextPage<String>(
      current: page(['a', 'b']),
      emit: emitted.add,
      fetch: (requested, existing) async {
        expect(requested, 2, reason: 'asks for the page AFTER the current one');
        expect(existing, ['a', 'b']);
        return page([...existing, 'c'], at: requested, total: 3, hasMore: false);
      },
    );

    expect(result!.items, ['a', 'b', 'c']);
    expect(emitted.first.loadingMore, isTrue,
        reason: 'the footer spins while the page is in flight');
    expect(emitted.last.loadingMore, isFalse);
    expect(emitted.last.hasMore, isFalse);
  });

  test('a failed page leaves the pages already read on screen', () async {
    final emitted = <PagedList<String>>[];

    await expectLater(
      loadNextPage<String>(
        current: page(['a', 'b']),
        emit: emitted.add,
        fetch: (_, __) async =>
            throw const ApiFailure(kind: ApiFailureKind.offline),
      ),
      throwsA(isA<ApiFailure>()),
    );

    // Two emissions: start working, then settle back to exactly what was held.
    expect(emitted.last.items, ['a', 'b']);
    expect(emitted.last.page, 1, reason: 'the page number did not advance');
    expect(emitted.last.loadingMore, isFalse);
    expect(emitted.last.hasMore, isTrue,
        reason: 'there is still more; the request failed, the list did not end');
  });

  test('nothing is requested when the server said there is no more', () async {
    var asked = false;

    final result = await loadNextPage<String>(
      current: page(['a'], total: 1, hasMore: false),
      emit: (_) => fail('nothing should be emitted'),
      fetch: (_, __) async {
        asked = true;
        return page(const []);
      },
    );

    expect(result, isNull);
    expect(asked, isFalse);
  });

  test('a second request is refused while one is in flight', () async {
    var asked = false;

    final result = await loadNextPage<String>(
      current: page(['a']).working(),
      emit: (_) => fail('nothing should be emitted'),
      fetch: (_, __) async {
        asked = true;
        return page(const []);
      },
    );

    expect(result, isNull);
    expect(asked, isFalse,
        reason: 'scrolling past the trigger twice must not fetch page 2 twice');
  });

  test('an empty list is empty, and knows the server total', () {
    const empty = PagedList<String>.empty();
    expect(empty.isEmpty, isTrue);
    expect(empty.total, 0);
    expect(empty.hasMore, isFalse);

    // The total is the SERVER's over the whole list, never the length held.
    final firstOfMany = page(['a', 'b'], total: 137);
    expect(firstOfMany.items.length, 2);
    expect(firstOfMany.total, 137);
  });
}
