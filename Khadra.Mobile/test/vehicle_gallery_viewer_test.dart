import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:khadra_mobile/core/widgets/khadra_widgets.dart';
import 'package:khadra_mobile/features/catalogue/vehicle_gallery_viewer.dart';
import 'package:khadra_mobile/l10n/app_localizations.dart';

/// The full-screen photograph viewer.
///
/// Every url here is one the SERVER would have sent for that car — the widget
/// takes the list it is given and shows exactly that, in that order.
///
/// No HTTP reaches anywhere in a widget test, so no photograph ever arrives.
/// That is the point rather than a limitation: it is the same state a phone on a
/// bad connection is in, and what these prove is that the viewer works in it —
/// the counter counts, the swipe pages, the close button closes, and nothing
/// throws. Drawing the frame itself is `KhadraImage`'s job, shared with every
/// other screen that shows a photograph.
void main() {
  final en = lookupAppLocalizations(const Locale('en'));
  final ar = lookupAppLocalizations(const Locale('ar'));

  /// Six photographs, as a gallery with a full set would have published them.
  const six = [
    'https://example.test/1.jpg',
    'https://example.test/2.jpg',
    'https://example.test/3.jpg',
    'https://example.test/4.jpg',
    'https://example.test/5.jpg',
    'https://example.test/6.jpg',
  ];

  Future<void> pumpViewer(
    WidgetTester tester, {
    required List<String> urls,
    int initialIndex = 0,
    Locale locale = const Locale('en'),
  }) async {
    tester.view.physicalSize = const Size(412, 915);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);

    await tester.pumpWidget(
      MaterialApp(
        locale: locale,
        supportedLocales: AppLocalizations.supportedLocales,
        localizationsDelegates: const [
          AppLocalizations.delegate,
          GlobalMaterialLocalizations.delegate,
          GlobalWidgetsLocalizations.delegate,
          GlobalCupertinoLocalizations.delegate,
        ],
        home: VehicleGalleryViewer(urls: urls, initialIndex: initialIndex),
      ),
    );
    await tester.pump();
  }

  /// Settles the frames an interaction needs.
  ///
  /// NOT `pumpAndSettle`: an image that has not loaded shows a spinner, and in a
  /// widget test nothing ever loads, so the tree never goes quiet. Every wait
  /// here is a bounded one.
  Future<void> settle(WidgetTester tester) async {
    await tester.pump();
    for (var frame = 0; frame < 10; frame++) {
      await tester.pump(const Duration(milliseconds: 100));
    }
  }

  /// A real double tap: two taps at one point, close enough in time to be one
  /// gesture. Two bare `tap` calls are two separate taps.
  Future<void> doubleTap(WidgetTester tester, Finder target) async {
    final centre = tester.getCenter(target);
    await tester.tapAt(centre);
    await tester.pump(const Duration(milliseconds: 50));
    await tester.tapAt(centre);
    await settle(tester);
  }

  /// Swipes to the next photograph the way a thumb does, in the direction the
  /// language reads.
  Future<void> swipeForward(WidgetTester tester, {required bool rtl}) async {
    await tester.drag(find.byType(PageView), Offset(rtl ? 380 : -380, 0));
    await settle(tester);
  }

  testWidgets('opens on the photograph that was tapped, not the first',
      (tester) async {
    await pumpViewer(tester, urls: six, initialIndex: 3);

    expect(find.text(en.vehiclePhotoPosition('4', '6')), findsOneWidget);
  });

  testWidgets('swiping moves through every photograph', (tester) async {
    await pumpViewer(tester, urls: six, initialIndex: 0);
    expect(find.text(en.vehiclePhotoPosition('1', '6')), findsOneWidget);

    await swipeForward(tester, rtl: false);
    expect(find.text(en.vehiclePhotoPosition('2', '6')), findsOneWidget);

    await swipeForward(tester, rtl: false);
    expect(find.text(en.vehiclePhotoPosition('3', '6')), findsOneWidget);
  });

  testWidgets('in Arabic the forward swipe is the one that reads forwards',
      (tester) async {
    // The pager takes its direction from the layout, so the gesture that
    // advances is the one an Arabic reader makes — and the COUNTER still counts
    // the list, so "2 / 6" means the second photograph either way.
    await pumpViewer(tester, urls: six, locale: const Locale('ar'));
    expect(find.text(ar.vehiclePhotoPosition('1', '6')), findsOneWidget);

    await swipeForward(tester, rtl: true);
    expect(find.text(ar.vehiclePhotoPosition('2', '6')), findsOneWidget);
  });

  testWidgets('the position is isolated so Arabic cannot reorder it',
      (tester) async {
    await pumpViewer(tester, urls: six, initialIndex: 1, locale: const Locale('ar'));

    // "2 / 6" reordered by bidi reads "6 / 2" — a position the reader is not at.
    final counter = find.ancestor(
      of: find.text(ar.vehiclePhotoPosition('2', '6')),
      matching: find.byType(Directionality),
    );
    expect(
      tester.widget<Directionality>(counter.first).textDirection,
      TextDirection.ltr,
    );
  });

  testWidgets('one photograph shows no counter and does not page', (tester) async {
    await pumpViewer(tester, urls: const ['https://example.test/only.jpg']);

    expect(find.text(en.vehiclePhotoPosition('1', '1')), findsNothing);
    // Still a pager, so the screen is the same shape; there is simply nowhere
    // to go, which is what a single-photograph car should feel like.
    await swipeForward(tester, rtl: false);
    expect(tester.takeException(), isNull);
  });

  testWidgets('a photograph that will not load raises nothing and keeps the frame',
      (tester) async {
    // Nothing resolves in a widget test, so this url behaves exactly as a
    // photograph the phone cannot fetch does: the viewer stays up, the counter
    // and the close button stay usable, and no exception escapes.
    //
    // WHAT it draws in that frame is `KhadraImage`'s business — a spinner while
    // it tries, the app's own empty frame once it fails — and it is the same
    // widget the catalogue, the saved list and the booking cards all use, so
    // the viewer inherits that behaviour rather than inventing its own.
    await pumpViewer(tester, urls: const [
      'https://example.test/missing.jpg',
      'https://example.test/2.jpg',
    ]);
    await tester.pump(const Duration(seconds: 1));

    expect(tester.takeException(), isNull);
    expect(find.byType(KhadraImage), findsWidgets);
    expect(find.text(en.vehiclePhotoPosition('1', '2')), findsOneWidget);

    // And a broken FIRST photograph does not block the rest of the gallery.
    await swipeForward(tester, rtl: false);
    expect(find.text(en.vehiclePhotoPosition('2', '2')), findsOneWidget);
    expect(tester.takeException(), isNull);
  });

  for (final locale in [const Locale('en'), const Locale('ar')]) {
    testWidgets('closes back onto the car it was opened from, in ${locale.languageCode}',
        (tester) async {
      // Pushed over a host, as it is over the vehicle screen, so closing has
      // somewhere to return to and the test proves it returns there.
      tester.view.physicalSize = const Size(412, 915);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.reset);

      late BuildContext host;
      await tester.pumpWidget(
        MaterialApp(
          locale: locale,
          supportedLocales: AppLocalizations.supportedLocales,
          localizationsDelegates: const [
            AppLocalizations.delegate,
            GlobalMaterialLocalizations.delegate,
            GlobalWidgetsLocalizations.delegate,
            GlobalCupertinoLocalizations.delegate,
          ],
          home: Builder(builder: (context) {
            host = context;
            return const Scaffold(body: Text('the car'));
          }),
        ),
      );

      unawaited(showVehicleGallery(host, urls: six, initialIndex: 2));
      await settle(tester);
      expect(find.byType(VehicleGalleryViewer), findsOneWidget);

      await tester.tap(find.byIcon(Icons.close_rounded));
      await settle(tester);

      expect(find.byType(VehicleGalleryViewer), findsNothing);
      expect(find.text('the car'), findsOneWidget);
      expect(tester.takeException(), isNull);
    });
  }

  /// How much the photograph on screen is magnified, read off what is drawn.
  ///
  /// The nearest `Transform` ABOVE the photograph — the pager has transforms of
  /// its own for the scroll offset, and those are always at natural size.
  double scaleOnScreen(WidgetTester tester) => tester
      .widget<Transform>(
        find.ancestor(of: find.byType(KhadraImage).first, matching: find.byType(Transform)).first,
      )
      .transform
      .getMaxScaleOnAxis();

  testWidgets('double tapping zooms in, and again zooms back out', (tester) async {
    await pumpViewer(tester, urls: six);

    expect(scaleOnScreen(tester), closeTo(1, 0.001));

    await doubleTap(tester, find.byType(PageView));
    expect(scaleOnScreen(tester), greaterThan(1.5));

    await doubleTap(tester, find.byType(PageView));
    expect(scaleOnScreen(tester), closeTo(1, 0.001));
  });

  testWidgets('a slow drag of many small moves still turns the page', (tester) async {
    // A thumb, not one synthetic jump: a stream of small moves across a frame
    // each, which is what a `PageView` under a gesture-hungry child has to
    // survive.
    await pumpViewer(tester, urls: six);

    final gesture = await tester.startGesture(tester.getCenter(find.byType(PageView)));
    for (var move = 0; move < 20; move++) {
      await gesture.moveBy(const Offset(-15, 0));
      await tester.pump(const Duration(milliseconds: 16));
    }
    await gesture.up();
    await settle(tester);

    expect(find.text(en.vehiclePhotoPosition('2', '6')), findsOneWidget);
  });

  testWidgets('a zoomed photograph does not page under the thumb', (tester) async {
    // The whole reason `_zoomed` exists. Dragging across a photograph the customer
    // has zoomed into is them looking at a corner of it, not asking for the next
    // one — and if the pager took that drag they would lose the zoom AND the
    // position in one gesture.
    await pumpViewer(tester, urls: six);
    expect(find.text(en.vehiclePhotoPosition('1', '6')), findsOneWidget);

    await doubleTap(tester, find.byType(PageView));
    await swipeForward(tester, rtl: false);
    expect(find.text(en.vehiclePhotoPosition('1', '6')), findsOneWidget);

    // And zooming back out hands the gesture back.
    await doubleTap(tester, find.byType(PageView));
    await swipeForward(tester, rtl: false);
    expect(find.text(en.vehiclePhotoPosition('2', '6')), findsOneWidget);
  });

  testWidgets('closing mid-zoom disposes cleanly', (tester) async {
    // The zoom animation is running when the route goes. Anything left listening
    // to a disposed controller throws here rather than in somebody's hands.
    await pumpViewer(tester, urls: six);
    final centre = tester.getCenter(find.byType(PageView));
    await tester.tapAt(centre);
    await tester.pump(const Duration(milliseconds: 50));
    await tester.tapAt(centre);
    // Straight out, one frame into the 180ms animation.
    await tester.pump(const Duration(milliseconds: 20));

    await tester.pumpWidget(const SizedBox());
    // Long enough for the image widget's own fade timer to retire; it is not this
    // widget's, and leaving it running fails the test framework's teardown check.
    await tester.pump(const Duration(seconds: 1));

    expect(tester.takeException(), isNull);
  });

  testWidgets('an empty gallery opens nothing at all', (tester) async {
    // `showVehicleGallery` is the door, and a car with no photographs has none
    // to open — the carousel behind it is already showing an empty frame.
    late BuildContext captured;
    await tester.pumpWidget(
      MaterialApp(
        supportedLocales: AppLocalizations.supportedLocales,
        localizationsDelegates: const [
          AppLocalizations.delegate,
          GlobalMaterialLocalizations.delegate,
          GlobalWidgetsLocalizations.delegate,
          GlobalCupertinoLocalizations.delegate,
        ],
        home: Builder(builder: (context) {
          captured = context;
          return const SizedBox();
        }),
      ),
    );

    await showVehicleGallery(captured, urls: const [], initialIndex: 0);
    await settle(tester);

    expect(find.byType(VehicleGalleryViewer), findsNothing);
  });
}
