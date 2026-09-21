import 'package:flutter_test/flutter_test.dart';
import 'package:khadra_mobile/features/catalogue/photo_gestures.dart';

/// Who owns a drag across a photograph inside a pager.
///
/// This rule is the whole of a defect that no widget test could see. Inside a
/// `PageView`, an `InteractiveViewer` puts a `ScaleGestureRecognizer` into the
/// gesture arena; a one-finger drag counts as a pan to it, so it declares itself
/// the winner before the pager's horizontal drag can, and then — at natural size,
/// with nowhere to pan — does nothing with what it won. The photograph did not
/// move and the page did not turn. The arena log from the device said it plainly:
///
///     Adding: ScaleGestureRecognizer(debugOwner: GestureDetector)
///     Adding: HorizontalDragGestureRecognizer
///     Self-declared winner: ScaleGestureRecognizer
///
/// `panEnabled: false` does not change that: it suppresses the effect, not the
/// claim. A synthesised drag resolves the arena differently from a real finger,
/// which is why every swipe test passed while the gallery was frozen on a phone.
///
/// So the RULE is pinned here, where it can be, and the recogniser that enforces
/// it is a handful of lines around it.
void main() {
  const far = handOverAfter + 1;

  group('one finger on a photograph at its natural size', () {
    test('belongs to the pager, because the photograph has nowhere to move', () {
      expect(
        dragBelongsToThePager(pointerCount: 1, magnified: false, travelled: far),
        isTrue,
      );
    });

    test('belongs to the photograph once it is magnified', () {
      expect(
        dragBelongsToThePager(pointerCount: 1, magnified: true, travelled: far),
        isFalse,
      );
    });
  });

  test('a finger that has barely moved is nobody\'s yet', () {
    // The two fingers of a pinch never land in the same millisecond, so for an
    // instant a pinch looks like a one-finger drag. Handing over on the very
    // first move would mean a pinch could never start.
    expect(
      dragBelongsToThePager(pointerCount: 1, magnified: false, travelled: 0),
      isFalse,
    );
    expect(
      dragBelongsToThePager(pointerCount: 1, magnified: false, travelled: handOverAfter),
      isFalse,
    );
  });

  test('the hand-over happens well before the pager would claim it anyway', () {
    // `kTouchSlop` is 18: the photograph must stand down before the horizontal
    // drag recogniser starts competing, or the race is back.
    expect(handOverAfter, lessThan(18));
  });

  group('two fingers', () {
    test('are always the photograph, magnified or not', () {
      // A pinch. The pager has no use for it, and taking it would mean the
      // customer could never magnify anything.
      for (final magnified in [true, false]) {
        expect(
          dragBelongsToThePager(pointerCount: 2, magnified: magnified, travelled: far),
          isFalse,
        );
        expect(
          dragBelongsToThePager(pointerCount: 3, magnified: magnified, travelled: far),
          isFalse,
        );
      }
    });
  });

  test('the recogniser asks at the moment of the gesture, not when it was built', () {
    var magnified = false;
    final recognizer = PagerFriendlyScaleRecognizer(magnified: () => magnified);
    addTearDown(recognizer.dispose);

    expect(recognizer.magnified(), isFalse);
    magnified = true;
    expect(recognizer.magnified(), isTrue);
  });
}
