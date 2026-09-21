import 'package:flutter/material.dart';
import 'package:flutter/gestures.dart';
import 'package:flutter/services.dart';

import '../../core/theme/khadra_theme.dart';
import '../../core/widgets/khadra_widgets.dart';
import '../../l10n/app_localizations.dart';
import 'photo_gestures.dart';

/// Opens the car's photographs full screen, starting on the one that was tapped.
///
/// A route on the ROOT navigator rather than a `go_router` path: this is a modal
/// over the car it belongs to, with nothing of its own to link to or restore, and
/// the system Back closes it the way a customer expects.
Future<void> showVehicleGallery(
  BuildContext context, {
  required List<String> urls,
  required int initialIndex,
}) {
  if (urls.isEmpty) return Future<void>.value();

  return Navigator.of(context, rootNavigator: true).push<void>(
    MaterialPageRoute<void>(
      fullscreenDialog: true,
      builder: (_) => VehicleGalleryViewer(urls: urls, initialIndex: initialIndex),
    ),
  );
}

/// Every photograph the gallery published for one car, full screen.
///
/// The images are the ones the server sent for THIS car — the same list the
/// carousel behind it is showing. Nothing is added, reordered or substituted,
/// and a photograph that will not load shows the same empty frame it shows
/// everywhere else rather than a broken-image glyph or a gap.
class VehicleGalleryViewer extends StatefulWidget {
  const VehicleGalleryViewer({super.key, required this.urls, required this.initialIndex})
    : assert(urls.length > 0, 'A gallery with no photographs has nothing to open.');

  final List<String> urls;
  final int initialIndex;

  @override
  State<VehicleGalleryViewer> createState() => _VehicleGalleryViewerState();
}

class _VehicleGalleryViewerState extends State<VehicleGalleryViewer> {
  late final PageController _pages;
  late int _index;

  /// Whether the photograph on screen is zoomed in.
  ///
  /// While it is, the pager stops scrolling: otherwise dragging to look at the
  /// corner of a zoomed photograph turns into a page change halfway through, and
  /// the customer loses both the position and the zoom.
  bool _zoomed = false;

  @override
  void initState() {
    super.initState();
    _index = widget.initialIndex.clamp(0, widget.urls.length - 1);
    _pages = PageController(initialPage: _index);
  }

  @override
  void dispose() {
    _pages.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);

    // No AppBar, so nothing else sets the status bar: without this the previous screen's dark icons
    // stay, and on a black photograph they are invisible.
    return AnnotatedRegion<SystemUiOverlayStyle>(
      value: SystemUiOverlayStyle.light,
      child: Scaffold(
        backgroundColor: Colors.black,
        body: Stack(
          children: [
            Positioned.fill(
              child: PageView.builder(
                controller: _pages,
                itemCount: widget.urls.length,
                // Null, not `PageScrollPhysics()`: passing one replaces the platform's own parent
                // physics, and the overscroll at either end stops behaving the way every other list
                // in the app does.
                physics: _zoomed ? const NeverScrollableScrollPhysics() : null,
                // The pager is NOT given a direction of its own: under an Arabic
                // layout Flutter reverses it, so the swipe that advances is the one
                // that reads forwards in the language on screen. The index stays
                // the list's own, so the counter means the same thing either way.
                onPageChanged: (index) => setState(() {
                  _index = index;
                  _zoomed = false;
                }),
                itemBuilder: (_, index) => _ZoomablePhoto(
                  key: ValueKey(index),
                  url: widget.urls[index],
                  // A photograph that is no longer the one on screen goes back to
                  // its natural size. Today `PageView` unmounts it anyway, so this
                  // changes nothing -- but the day anybody turns on
                  // `allowImplicitScrolling`, neighbours stay alive with their zoom
                  // intact, and swiping back to one would leave `_zoomed` false over
                  // a zoomed photograph: the pager would page while the customer
                  // meant to pan. The flag has to be true OF THE CURRENT PAGE, so
                  // the current page is what owns the reset.
                  isCurrent: index == _index,
                  onZoomChanged: (zoomed) {
                    if (zoomed != _zoomed) setState(() => _zoomed = zoomed);
                  },
                ),
              ),
            ),

            // The chrome, over the photograph and clear of the notch.
            PositionedDirectional(
              top: 0,
              start: 0,
              end: 0,
              child: SafeArea(
                bottom: false,
                child: Padding(
                  padding: const EdgeInsets.symmetric(horizontal: Space.sm, vertical: Space.sm),
                  child: Row(
                    children: [
                      _Round(
                        icon: Icons.close_rounded,
                        label: l10n.actionClose,
                        onTap: () => Navigator.of(context).maybePop(),
                      ),
                      const Spacer(),
                      if (widget.urls.length > 1)
                        Container(
                          padding: const EdgeInsets.symmetric(horizontal: Space.md, vertical: 6),
                          decoration: BoxDecoration(
                            color: Colors.black.withValues(alpha: 0.55),
                            borderRadius: Radii.pill,
                          ),
                          // Isolated: digits and a solidus inside an Arabic layout
                          // would otherwise be reordered, and "2 / 6" would read
                          // "6 / 2" — a position that is not where the reader is.
                          child: LatinRun(
                            l10n.vehiclePhotoPosition(
                              (_index + 1).toString(),
                              widget.urls.length.toString(),
                            ),
                            style: const TextStyle(
                              color: Colors.white,
                              fontSize: 13,
                              fontWeight: FontWeight.w700,
                            ),
                          ),
                        ),
                    ],
                  ),
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

/// One photograph: pinchable, pannable, double-tappable — and willing to let go.
///
/// Deliberately NOT an `InteractiveViewer`. That widget always puts a
/// `ScaleGestureRecognizer` into the gesture arena, and a one-finger drag is a
/// pan as far as it is concerned, so it declares itself the winner before the
/// pager's horizontal drag can and the gallery stops swiping. `panEnabled: false`
/// does not help: the recogniser still enters, still wins, and then does nothing
/// with what it won. See `photo_gestures.dart`.
///
/// So the transform is kept here, driven by a recogniser that stands down for a
/// one-finger drag on a photograph at its natural size.
class _ZoomablePhoto extends StatefulWidget {
  const _ZoomablePhoto({
    super.key,
    required this.url,
    required this.isCurrent,
    required this.onZoomChanged,
  });

  final String url;

  /// Whether this is the photograph the pager is showing. A photograph that
  /// stops being current gives up its magnification.
  final bool isCurrent;

  final ValueChanged<bool> onZoomChanged;

  @override
  State<_ZoomablePhoto> createState() => _ZoomablePhotoState();
}

class _ZoomablePhotoState extends State<_ZoomablePhoto> with SingleTickerProviderStateMixin {
  /// How far a pinch may go. Past this the photograph is mostly grain.
  static const double _maxScale = 5;

  /// What a double tap zooms to. Enough to read a number plate, short of the
  /// pinch ceiling so there is somewhere left to go by hand.
  static const double _doubleTapScale = 2.5;

  /// Anything this close to 1 is not magnified: a pinch that lands a hair off
  /// natural size must not leave the photograph holding the pager's swipe.
  static const double _magnifiedAbove = 1.01;

  /// The photograph's size, then its position, applied in that order: a point
  /// `p` of the child is drawn at `p * _scale + _offset`.
  double _scale = 1;
  Offset _offset = Offset.zero;

  /// Where the gesture began, so the whole of it is measured from one origin
  /// rather than accumulating rounding from every update.
  double _scaleAtStart = 1;
  Offset _offsetAtStart = Offset.zero;
  Offset _focalAtStart = Offset.zero;

  late final AnimationController _animation;
  late final CurvedAnimation _curve;
  Animation<double>? _scaleRun;
  Animation<Offset>? _offsetRun;

  Size _viewport = Size.zero;

  bool get _magnified => _scale > _magnifiedAbove;

  @override
  void initState() {
    super.initState();
    _animation = AnimationController(vsync: this, duration: const Duration(milliseconds: 180))
      ..addListener(() {
        final scale = _scaleRun;
        final offset = _offsetRun;
        if (scale == null || offset == null) return;
        setState(() {
          _scale = scale.value;
          _offset = offset.value;
        });
        _announce();
      });
    // One curve for the life of the photograph: a `CurvedAnimation` registers a
    // status listener on its parent, so minting a fresh one per double tap would
    // leave one behind on every tap.
    _curve = CurvedAnimation(parent: _animation, curve: Curves.easeOut);
  }

  @override
  void didUpdateWidget(_ZoomablePhoto old) {
    super.didUpdateWidget(old);
    // A photograph that is no longer the one on screen goes back to its natural
    // size. Today `PageView` unmounts it anyway, so this changes nothing — but
    // the day anybody turns on `allowImplicitScrolling`, neighbours stay alive
    // with their magnification, and swiping back to one would leave the pager
    // thinking it was free to page while the customer meant to pan.
    if (old.isCurrent && !widget.isCurrent && (_magnified || _offset != Offset.zero)) {
      _animation.stop();
      _scaleRun = null;
      _offsetRun = null;
      // Assigned rather than through `setState`: this runs inside the build
      // phase, where `setState` is an error, and a rebuild is already under way.
      _scale = 1;
      _offset = Offset.zero;
    }
  }

  @override
  void dispose() {
    _curve.dispose();
    _animation.dispose();
    super.dispose();
  }

  /// Tells the pager whether it may page — but only for the photograph on
  /// screen: a neighbour still settling would otherwise answer for a page the
  /// customer is no longer on.
  void _announce() {
    if (widget.isCurrent) widget.onZoomChanged(_magnified);
  }

  /// Keeps the photograph covering the screen it is shown on.
  ///
  /// At natural size that pins it exactly; magnified, it may be moved until an
  /// edge reaches the edge of the screen and no further, so a band of background
  /// never opens up beside it.
  Offset _within(Offset offset, double scale) {
    final minX = _viewport.width - _viewport.width * scale;
    final minY = _viewport.height - _viewport.height * scale;
    return Offset(
      offset.dx.clamp(minX <= 0 ? minX : 0, 0),
      offset.dy.clamp(minY <= 0 ? minY : 0, 0),
    );
  }

  void _onScaleStart(ScaleStartDetails details) {
    _animation.stop();
    _scaleRun = null;
    _offsetRun = null;
    _scaleAtStart = _scale;
    _offsetAtStart = _offset;
    _focalAtStart = details.localFocalPoint;
  }

  void _onScaleUpdate(ScaleUpdateDetails details) {
    final scale = (_scaleAtStart * details.scale).clamp(1.0, _maxScale);
    // Whatever was under the fingers when the gesture began stays under them:
    // pinching around a headlight keeps the headlight where it is, and moving the
    // fingers afterwards carries it along.
    final anchored = (_focalAtStart - _offsetAtStart) / _scaleAtStart;
    final offset = _within(details.localFocalPoint - anchored * scale, scale);

    setState(() {
      _scale = scale;
      _offset = offset;
    });
    _announce();
  }

  void _animateTo(double scale, Offset offset) {
    _scaleRun = Tween<double>(begin: _scale, end: scale).animate(_curve);
    _offsetRun = Tween<Offset>(begin: _offset, end: offset).animate(_curve);
    _animation.forward(from: 0);
  }

  /// Zooms towards the point that was tapped, or back out if already magnified.
  void _onDoubleTap(TapDownDetails details) {
    if (_magnified) {
      _animateTo(1, Offset.zero);
      return;
    }

    final tap = details.localPosition;
    final anchored = (tap - _offset) / _scale;
    _animateTo(_doubleTapScale, _within(tap - anchored * _doubleTapScale, _doubleTapScale));
  }

  TapDownDetails? _lastTap;

  @override
  Widget build(BuildContext context) => LayoutBuilder(
    builder: (context, constraints) {
      _viewport = constraints.biggest;
      return RawGestureDetector(
        behavior: HitTestBehavior.opaque,
        gestures: <Type, GestureRecognizerFactory>{
          PagerFriendlyScaleRecognizer:
              GestureRecognizerFactoryWithHandlers<PagerFriendlyScaleRecognizer>(
                () => PagerFriendlyScaleRecognizer(magnified: () => _magnified, debugOwner: this),
                (recognizer) => recognizer
                  ..onStart = _onScaleStart
                  ..onUpdate = _onScaleUpdate,
              ),
          DoubleTapGestureRecognizer:
              GestureRecognizerFactoryWithHandlers<DoubleTapGestureRecognizer>(
                () => DoubleTapGestureRecognizer(debugOwner: this),
                (recognizer) => recognizer
                  ..onDoubleTapDown = ((details) {
                    _lastTap = details;
                  })
                  ..onDoubleTap = () {
                    final tap = _lastTap;
                    if (tap != null) _onDoubleTap(tap);
                  },
              ),
        },
        child: Transform(
          transform: Matrix4.identity()
            ..translateByDouble(_offset.dx, _offset.dy, 0, 1)
            ..scaleByDouble(_scale, _scale, _scale, 1),
          // The whole screen, not the photograph's own size.
          //
          // `Center` would hand the image loose constraints, and an image then
          // draws at its intrinsic size: a photograph smaller than the screen
          // appears as a small picture adrift in the middle of it, which in a
          // FULL-SCREEN viewer reads as a fault. Expanded, every photograph is
          // fitted to the screen and letterboxed.
          child: SizedBox.expand(
            // `contain`, not `cover`: a viewer exists to show the whole
            // photograph, and cropping it would defeat the tap that opened it.
            child: KhadraImage(url: widget.url, fit: BoxFit.contain),
          ),
        ),
      );
    },
  );
}

/// A round button on a dark photograph.
class _Round extends StatelessWidget {
  const _Round({required this.icon, required this.label, required this.onTap});

  final IconData icon;
  final String label;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) => Semantics(
    button: true,
    label: label,
    child: Material(
      color: Colors.black.withValues(alpha: 0.55),
      shape: const CircleBorder(),
      clipBehavior: Clip.antiAlias,
      child: InkWell(
        onTap: onTap,
        child: Padding(
          padding: const EdgeInsets.all(Space.sm),
          child: Icon(icon, color: Colors.white, size: 22),
        ),
      ),
    ),
  );
}
