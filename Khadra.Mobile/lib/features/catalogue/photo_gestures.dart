import 'package:flutter/gestures.dart';

/// How far one finger may travel before the photograph gives the drag up.
///
/// Not zero. The two fingers of a pinch never land in the same millisecond, so
/// for an instant a pinch looks exactly like a one-finger drag — standing down on
/// the very first move would mean a pinch could never start. A few pixels is long
/// enough for the second finger to arrive and short enough to be well inside
/// `kTouchSlop`, which is where the pager's own recogniser starts claiming.
const double handOverAfter = 6;

/// Who owns a drag across a photograph that sits inside a pager.
///
/// Only one of them can have it. A photograph at its natural size has nowhere to
/// move, so a drag across it is the customer asking for the next photograph; a
/// magnified one has somewhere to move, and the same drag is them looking around
/// inside it. Two fingers are always the photograph's — that is a pinch, and the
/// pager has no use for it.
bool dragBelongsToThePager({
  required int pointerCount,
  required bool magnified,
  required double travelled,
}) =>
    pointerCount < 2 && !magnified && travelled > handOverAfter;

/// A scale recogniser that gets out of the pager's way.
///
/// `ScaleGestureRecognizer` treats a one-finger drag as a pan and declares itself
/// the winner of the gesture arena before the pager's horizontal-drag recogniser
/// can. Inside a `PageView` that means an ordinary `InteractiveViewer` silently
/// eats every swipe — and turning its `panEnabled` off does NOT help, because the
/// recogniser still enters the arena and still wins, then does nothing with what
/// it won. The photograph does not move and the page does not turn.
///
/// The arena log from a real device said it in three lines:
///
/// ```
/// Adding: ScaleGestureRecognizer(debugOwner: GestureDetector)
/// Adding: HorizontalDragGestureRecognizer
/// Self-declared winner: ScaleGestureRecognizer
/// ```
///
/// Widget tests cannot see this: a synthesised drag resolves the arena
/// differently from a real finger, so the swipe test passed on the bench while
/// the gallery was frozen on the phone. What IS testable is the rule —
/// [dragBelongsToThePager] — and it is pinned on its own.
class PagerFriendlyScaleRecognizer extends ScaleGestureRecognizer {
  PagerFriendlyScaleRecognizer({required this.magnified, super.debugOwner});

  /// Whether the photograph is currently magnified, asked at the moment of the
  /// gesture rather than captured when the recogniser was built.
  final bool Function() magnified;

  Offset? _from;
  bool _stoodDown = false;

  @override
  void addAllowedPointer(PointerDownEvent event) {
    _from ??= event.position;
    super.addAllowedPointer(event);
  }

  @override
  void handleEvent(PointerEvent event) {
    // Once this sequence has been handed over it is not ours to interpret, and
    // `super` must not see it either: rejecting resets the recogniser, and its
    // own `handleEvent` asserts that it is not in that state.
    if (_stoodDown) return;

    if (event is PointerMoveEvent) {
      final from = _from;
      if (from != null &&
          dragBelongsToThePager(
            pointerCount: pointerCount,
            magnified: magnified(),
            travelled: (event.position - from).distance,
          )) {
        _stoodDown = true;
        resolve(GestureDisposition.rejected);
        return;
      }
    }

    super.handleEvent(event);
  }

  @override
  void didStopTrackingLastPointer(int pointer) {
    _from = null;
    _stoodDown = false;
    super.didStopTrackingLastPointer(pointer);
  }
}
