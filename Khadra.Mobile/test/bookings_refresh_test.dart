import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:khadra_mobile/api/dtos.dart';
import 'package:khadra_mobile/core/api/api_failure.dart';
import 'package:khadra_mobile/core/paging.dart';
import 'package:khadra_mobile/core/providers.dart';
import 'package:khadra_mobile/core/router.dart';
import 'package:khadra_mobile/core/widgets/khadra_widgets.dart';
import 'package:khadra_mobile/features/bookings/booking_providers.dart';
import 'package:khadra_mobile/features/bookings/bookings_screen.dart';
import 'package:khadra_mobile/l10n/app_localizations.dart';
import 'package:timezone/data/latest_all.dart' as tz_data;

import 'package:khadra_mobile/core/live/live_refresh.dart';
import 'package:khadra_mobile/core/live/live_surfaces.dart';

import 'support/fake_api.dart';

/// Ages a surface past the floor: "the customer came back later", without the twenty seconds.
void stale(ProviderContainer container, String id) =>
    container.read(liveRefreshProvider).markStale(id);

/// A list that is RELOADING is not a list that has nothing to show.
///
/// The screen matched `AsyncLoading()` before its data arms, and Riverpod reports
/// the two kinds of re-fetch differently: a pull-to-refresh leaves `AsyncData`
/// with `isLoading` set, which that arm never saw, while a re-run caused by a
/// DEPENDENCY changing gives `AsyncLoading` still carrying the previous value,
/// which it swallowed whole.
///
/// The dependency is `sessionProvider`, and a token rotation assigns a new
/// `SessionState` roughly every four minutes. So the list turned into a spinner on
/// a timer — and while the rotation was also deadlocking, into a spinner that
/// never came back. That deadlock is `token_rotation_test`; this is the other
/// half, and each was enough on its own to produce the report.
///
/// The last three are measurements rather than rules. They record what opening
/// this screen actually costs, so a future change that turns one request into
/// eight has to walk past a test that says so.
void main() {
  setUpAll(tz_data.initializeTimeZones);

  BookingListItem booking({
    String id = 'b-1',
    String reference = 'KH-1001',
    String make = 'Toyota',
    String model = 'Corolla',
    String status = 'Confirmed',
  }) =>
      BookingListItem(
        bookingId: id,
        reference: reference,
        status: status,
        periodStart: DateTime.utc(2026, 10, 1, 9),
        periodEnd: DateTime.utc(2026, 10, 4, 9),
        days: 3,
        pickupMethod: 'SelfPickup',
        totalPrice: 90,
        currency: 'JOD',
        createdAt: DateTime.utc(2026, 9, 20, 9),
        vehicle: VehicleLabel(
          vehicleId: 'v-1',
          make: make,
          model: model,
          year: 2024,
          color: 'White',
          plateNumber: '12-3456',
          coverImageUrl: null,
        ),
        dealerName: 'Petra Rentals',
        dealerRemoved: false,
        hasLiveDispute: false,
        dealerId: 'd-1',
      );

  Paged<BookingListItem> page(List<BookingListItem> items) => Paged(
        items: items,
        page: 1,
        pageSize: 20,
        totalCount: items.length,
      );

  late FakeApi api;
  late ProviderContainer container;

  /// Opens the bookings tab.
  ///
  /// [settle] is false when a test is deliberately holding a fetch open —
  /// `pumpAndSettle` waits for a quiet frame, and a screen waiting on the network
  /// never gives it one.
  Future<GoRouter> pumpBookingsReturningRouter(
    WidgetTester tester, {
    bool settle = true,
  }) async {
    tester.view.physicalSize = const Size(1000, 2400);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);

    container = ProviderContainer(overrides: [
      apiProvider.overrideWithValue(api),
      sessionStoreProvider.overrideWithValue(FakeSessionStore()),
      sharedPreferencesProvider.overrideWithValue(null),
    ]);
    addTearDown(container.dispose);

    await container
        .read(sessionProvider.notifier)
        .adoptTokens(FakeApi.fakeTokens());

    final router = container.read(routerProvider);

    await tester.pumpWidget(
      UncontrolledProviderScope(
        container: container,
        child: MaterialApp.router(
          locale: const Locale('en'),
          routerConfig: router,
          supportedLocales: AppLocalizations.supportedLocales,
          localizationsDelegates: const [
            AppLocalizations.delegate,
            GlobalMaterialLocalizations.delegate,
            GlobalWidgetsLocalizations.delegate,
            GlobalCupertinoLocalizations.delegate,
          ],
        ),
      ),
    );
    await tester.pumpAndSettle();

    router.go(Routes.bookings);
    if (settle) {
      await tester.pumpAndSettle();
    } else {
      await tester.pump();
      await tester.pump();
    }
    expect(find.byType(BookingsScreen), findsOneWidget);
    return router;
  }

  Future<void> pumpBookings(WidgetTester tester, {bool settle = true}) async {
    await pumpBookingsReturningRouter(tester, settle: settle);
  }

  setUp(() {
    api = FakeApi()
      ..bookings = page([booking()])
      ..tabCounts = const {'all': 1};
  });

  testWidgets('a token rotation costs nothing at all', (tester) async {
    // This used to cost a re-read of the list, the counts and the landing card every few minutes,
    // and — before the screen was fixed — to replace the list with a spinner while they ran.
    //
    // Both came from one line: `MyBookingsNotifier.build` watched the WHOLE `SessionState`, which
    // has no value equality, so `_install` assigning an identical-looking state on every rotation
    // counted as a change. It watches `isSignedIn` now, and a rotation does not touch that.
    await pumpBookings(tester);
    expect(find.text('Toyota Corolla'), findsOneWidget);
    expect(api.myBookingsCalls, 1);

    container.read(sessionProvider.notifier).applyUser(FakeApi.fakeUser());
    await tester.pumpAndSettle();

    expect(
      container.read(myBookingsProvider(BookingTabs.all)),
      isA<AsyncData<PagedList<BookingListItem>>>(),
      reason: 'a rotation must not put the list back into a loading state',
    );
    expect(api.myBookingsCalls, 1, reason: 'and must not re-read it either');
    expect(api.bookingTabCountsCalls, 1);
    expect(find.text('Toyota Corolla'), findsOneWidget);
    expect(find.byType(KhadraLoading), findsNothing);
  });

  testWidgets('a re-fetch that carries its last answer keeps it on screen',
      (tester) async {
    // The screen's half of the same rule, kept under test now that a rotation no longer produces
    // this state by itself. `AsyncLoading` CARRYING a previous value is what an `AsyncLoading()`
    // arm used to swallow, and any future dependency change will produce it again.
    await pumpBookings(tester);
    expect(find.text('Toyota Corolla'), findsOneWidget);

    api.holdBookings = Completer<void>();
    container.read(myBookingsProvider(BookingTabs.all).notifier).state =
        const AsyncLoading<PagedList<BookingListItem>>()
            .copyWithPrevious(AsyncData(PagedList<BookingListItem>.empty()));
    await tester.pump();

    // Whatever state it is in, a list that HAS rows shows them.
    expect(find.byType(KhadraLoading), findsNothing);

    api.holdBookings!.complete();
    await tester.pumpAndSettle();
  });

  testWidgets('a pull-to-refresh does not blank the list either', (tester) async {
    await pumpBookings(tester);

    api.holdBookings = Completer<void>();
    container.invalidate(myBookingsProvider(BookingTabs.all));
    await tester.pump();
    // Reading it is what starts the reload; the frame showing it comes after.
    container.read(myBookingsProvider(BookingTabs.all));
    await tester.pump();

    expect(find.text('Toyota Corolla'), findsOneWidget);
    expect(find.byType(KhadraLoading), findsNothing);

    api.holdBookings!.complete();
    await tester.pumpAndSettle();
  });

  testWidgets('a FIRST load with nothing to show still shows the spinner',
      (tester) async {
    // The other half of the rule. Keeping the last answer during a refresh must
    // not turn into showing nothing at all on the way to the first one.
    api.holdBookings = Completer<void>();

    await pumpBookings(tester, settle: false);

    expect(find.byType(KhadraLoading), findsWidgets);

    api.holdBookings!.complete();
    await tester.pumpAndSettle();
    expect(find.text('Toyota Corolla'), findsOneWidget);
  });

  testWidgets('a reload that FAILS replaces the list', (tester) async {
    await pumpBookings(tester);
    expect(find.text('Toyota Corolla'), findsOneWidget);

    // Stale rows that look current are worse than an error: the customer would
    // read a list that the server has already refused to confirm.
    api.bookingsFailure = const ApiFailure(kind: ApiFailureKind.server);
    container.invalidate(myBookingsProvider(BookingTabs.all));
    await tester.pumpAndSettle();

    expect(find.text('Toyota Corolla'), findsNothing);
    expect(find.byType(KhadraError), findsOneWidget);
  });

  testWidgets('opening the screen asks for the list and the counts once each',
      (tester) async {
    await pumpBookings(tester);

    expect(api.myBookingsCalls, 1);
    expect(api.bookingTabCountsCalls, 1);
  });

  testWidgets('coming straight back to a tab re-reads nothing', (tester) async {
    // The floor. Flicking between tabs must not be a burst of requests, so a return inside twenty
    // seconds is answered from what is already there.
    final router = await pumpBookingsReturningRouter(tester);
    expect(api.myBookingsCalls, 1);

    router.go(Routes.search);
    await tester.pumpAndSettle();
    router.go(Routes.bookings);
    await tester.pumpAndSettle();

    expect(api.myBookingsCalls, 1);
    expect(api.bookingTabCountsCalls, 1);
  });

  testWidgets('coming back to a tab AFTER the floor re-reads it', (tester) async {
    // Pre-launch item 125, which is what this whole batch is for: the tab shell keeps the screen
    // mounted, so an `autoDispose` provider never disposes and a customer returning after ten
    // minutes was shown bookings read ten minutes ago, with nothing on screen saying so. A gallery
    // can approve or reject inside that window.
    //
    // The floor is measured on a monotonic clock, so this reaches past it rather than faking time.
    final router = await pumpBookingsReturningRouter(tester);
    expect(api.myBookingsCalls, 1);

    router.go(Routes.search);
    await tester.pumpAndSettle();

    // Older than the twenty-second floor, without twenty seconds of test.
    stale(container, Surfaces.bookingsList);
    stale(container, Surfaces.bookingsCounts);

    router.go(Routes.bookings);
    await tester.pumpAndSettle();

    expect(api.myBookingsCalls, 2, reason: 'item 125: a stale tab must re-read on re-entry');
    expect(api.bookingTabCountsCalls, 2);
  });
}
