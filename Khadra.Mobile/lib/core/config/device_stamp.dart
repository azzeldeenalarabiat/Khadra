/// How this build names the device it is running on, per platform.
///
/// Conditional export rather than a `kIsWeb` branch: `dart:io` cannot be
/// COMPILED for the web at all, so a single file importing it would break the
/// web build regardless of which branch ran.
library;

export 'device_stamp_io.dart'
    if (dart.library.js_interop) 'device_stamp_web.dart';
