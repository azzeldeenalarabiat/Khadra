// Prepares the two source images `flutter_launcher_icons` slices into the
// launcher icons. Build-time only; nothing here ships.
//
//     dart run tools/make_launcher_icons.dart
//     dart run flutter_launcher_icons
//
// It exists because an adaptive icon is not "the logo in a PNG". Android hands the
// foreground layer to a launcher that crops it to a shape it chooses — a circle, a
// squircle, a rounded square, a teardrop — and only the CENTRAL 66 of the 108dp
// canvas is guaranteed to survive. Dropping `khadra-logo.png` straight in loses
// the "CAR RENTAL | تأجير سيارات" line on most phones and the wordmark on some.
//
// So the logo is trimmed to its own ink, scaled to fit that safe circle, and
// centred — once, reproducibly, so a redrawn logo becomes one command rather than
// eleven files cut by hand.
import 'dart:io';
import 'dart:math';

import 'package:image/image.dart' as img;

/// The artwork, exactly as the owner supplied it. Nothing here edits it.
const _source = 'assets/brand/khadra-logo.png';

const _foregroundOut = 'tools/launcher/foreground.png';
const _legacyOut = 'tools/launcher/legacy.png';

/// Android's adaptive canvas is 108dp and the guaranteed-visible area is the
/// central 66dp. Everything outside that belongs to whatever mask the launcher
/// happens to apply, so nothing that has to be read may live there.
const _canvas = 1024;
const _safeFraction = 66 / 108;

/// `flutter_launcher_icons` wraps the foreground in `<inset android:inset="16%">`
/// when it writes `mipmap-anydpi-v26/ic_launcher.xml`, so the mark this file draws
/// is shrunk again before any phone sees it.
///
/// This is here rather than being edited out of the generated XML because the XML
/// is REGENERATED: deleting the inset would work until the next time somebody ran
/// the tool, and then the icon would quietly grow past the safe zone with nothing
/// to say why. Two insets that know about each other are safer than one that gets
/// reverted. If the tool ever stops adding it, this constant becomes 0 and the
/// numbers below still say what they mean.
const _generatorInset = 0.16;

/// `KhadraColors.price` — the deep brand green the profile monogram is drawn on.
/// Stated here as bytes because a Gradle resource cannot import a Dart file; the
/// generated `values/colors.xml` carries the same value and
/// `test/launcher_icon_test.dart` is what keeps the two honest.
const _background = (r: 0x14, g: 0x53, b: 0x2D);

void main() {
  final source = img.decodePng(File(_source).readAsBytesSync());
  if (source == null) {
    stderr.writeln('Could not read $_source');
    exitCode = 1;
    return;
  }

  // The badge is round and the PNG is square, and the corners turned out to be
  // OPAQUE WHITE rather than transparent — so trimming finds nothing to remove and
  // an adaptive foreground built from it is a white square floating on the green
  // background layer, which is not what the logo looks like.
  //
  // So the square is cut back to the badge it contains. The mark is inscribed in
  // its own PNG, so a circle of half the width is the badge's own edge; the last
  // pixel and a half fades rather than stepping, because a hard cut at this size
  // reads as a jagged rim once the launcher scales it down.
  final rounded = _circleMasked(img.trim(source, mode: img.TrimMode.transparent));
  final content = rounded;

  // Drawn LARGER than the safe zone by exactly as much as the generator will
  // shrink it, so the badge lands on 66/108 of the real canvas.
  final safe = (_canvas * _safeFraction / (1 - _generatorInset * 2)).round();
  final scale = safe / (content.width > content.height ? content.width : content.height);
  final mark = img.copyResize(
    content,
    width: (content.width * scale).round(),
    height: (content.height * scale).round(),
    interpolation: img.Interpolation.cubic,
  );

  final offsetX = ((_canvas - mark.width) / 2).round();
  final offsetY = ((_canvas - mark.height) / 2).round();

  // The adaptive FOREGROUND: transparent, because the background is a flat colour
  // layer underneath that the launcher may parallax independently.
  final foreground = img.Image(width: _canvas, height: _canvas, numChannels: 4);
  img.compositeImage(foreground, mark, dstX: offsetX, dstY: offsetY);
  File(_foregroundOut).writeAsBytesSync(img.encodePng(foreground));

  // The LEGACY icon, for Android 7 and earlier, which has no layers and applies no
  // mask: it gets the background painted in, so the mark never sits on whatever
  // the launcher's own backdrop happens to be.
  final legacy = img.Image(width: _canvas, height: _canvas, numChannels: 4);
  img.fill(
    legacy,
    color: img.ColorRgba8(_background.r, _background.g, _background.b, 255),
  );
  img.compositeImage(legacy, mark, dstX: offsetX, dstY: offsetY);
  File(_legacyOut).writeAsBytesSync(img.encodePng(legacy));

  stdout
    ..writeln('source   ${source.width}x${source.height}')
    ..writeln('masked   ${content.width}x${content.height}')
    ..writeln('mark     ${mark.width}x${mark.height} on a $_canvas canvas '
        '(safe zone $safe)')
    ..writeln('wrote    $_foregroundOut')
    ..writeln('wrote    $_legacyOut');
}

/// Clears everything outside the circle the image inscribes.
///
/// The alpha ramps over the outermost pixel and a half instead of stepping to
/// zero: a hard edge on a 1024px master is a visibly ragged rim once a launcher
/// has resized it to 48dp.
img.Image _circleMasked(img.Image source) {
  final masked = source.convert(numChannels: 4);
  final centre = (x: masked.width / 2, y: masked.height / 2);
  final radius = (masked.width < masked.height ? masked.width : masked.height) / 2;
  const feather = 1.5;

  for (var y = 0; y < masked.height; y++) {
    for (var x = 0; x < masked.width; x++) {
      final dx = x + 0.5 - centre.x;
      final dy = y + 0.5 - centre.y;
      final distance = sqrt(dx * dx + dy * dy);
      if (distance <= radius - feather) continue;

      final pixel = masked.getPixel(x, y);
      final fade = distance >= radius
          ? 0.0
          : (radius - distance) / feather;
      masked.setPixelRgba(
        x,
        y,
        pixel.r.toInt(),
        pixel.g.toInt(),
        pixel.b.toInt(),
        (pixel.a * fade).round(),
      );
    }
  }

  return masked;
}
