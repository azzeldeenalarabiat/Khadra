import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:go_router/go_router.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../config/app_environment.dart';

/// TEMPORARY — STAGING BUILDS ONLY. The W4-9 device investigation (2026-10-07): a
/// rejected-document push arrived on a physical phone and tapping it did nothing, while
/// every test of the same path passed. This records what actually reaches the app on a
/// phone, at each step from arrival to the screen opened, so the next Staging build can
/// say where the tap stops. Remove it once a push tap is verified on a device
/// (pre-launch checklist item 27).
///
/// **What it never holds**: a token, a reason, a name, a document's type or contents, or
/// an id. A push's data is reduced to its KEYS, its `kind` (a fixed word the server
/// chooses) and whether a subject was present; a route keeps its shape with every id
/// replaced by `:id`.
///
/// Kept on the phone (shared preferences) so a closed-app launch and the background
/// isolate leave their lines too, and read from Profile, on a Staging build only.
abstract final class PushTrace {
  /// On for a Staging build and off everywhere else, production included. A field so a
  /// test can switch it on.
  static bool enabled = AppEnvironment.isStaging;

  static const _key = 'khadra.push_trace';
  static const _backgroundKey = 'khadra.push_trace.background';

  /// Written by `MainActivity` (Kotlin) as each intent reaches the app, BEFORE FlutterFire or
  /// the local-notification plugin sees it: one string of lines. Read here, never written.
  static const _nativeKey = 'khadra.push_trace.native';
  static const _max = 80;

  static final List<String> _lines = [];
  static SharedPreferences? _preferences;

  /// Bumped on every new line, so an open diagnostics screen follows along.
  static final ValueNotifier<int> changes = ValueNotifier<int>(0);

  /// Loads what earlier runs left. Called once at start-up.
  static Future<void> attach() async {
    if (!enabled) return;
    try {
      _preferences = await SharedPreferences.getInstance();
      final saved = _preferences!.getStringList(_key) ?? const <String>[];
      _lines.insertAll(0, saved.where((line) => !_lines.contains(line)));
      _trim();
      changes.value++;
    } on Object {
      // A trace that cannot be stored is still printed.
    }
  }

  /// This process's lines, oldest first, including those carried over from earlier runs.
  static List<String> get lines => List.unmodifiable(_lines);

  /// What the background isolate wrote: a push that arrived while the app was not in front.
  static Future<List<String>> backgroundLines() async {
    if (!enabled) return const [];
    try {
      final preferences = _preferences ?? await SharedPreferences.getInstance();
      await preferences.reload();
      return preferences.getStringList(_backgroundKey) ?? const <String>[];
    } on Object {
      return const [];
    }
  }

  /// What Android delivered to the activity: the native side's own lines.
  static Future<List<String>> nativeLines() async {
    if (!enabled) return const [];
    try {
      final preferences = _preferences ?? await SharedPreferences.getInstance();
      await preferences.reload();
      final text = preferences.getString(_nativeKey) ?? '';
      return text.split('\n').where((line) => line.isNotEmpty).toList();
    } on Object catch (error) {
      return ['native lines unreadable: ${error.runtimeType}'];
    }
  }

  /// Records one step. [data] is reduced by [describe]; [detail] must already be safe.
  static void record(String stage, {Map<String, String?>? data, String? detail}) {
    if (!enabled) return;
    final line = _line(stage, data, detail);
    debugPrint('[push-trace] $line');
    _lines.add(line);
    _trim();
    changes.value++;
    unawaited(_save(_key, _lines));
  }

  /// The same, from the background isolate, which has no share in this one's memory.
  static Future<void> recordInBackground(String stage, {Map<String, String?>? data, String? detail}) async {
    if (!enabled) return;
    final line = _line(stage, data, detail);
    debugPrint('[push-trace] (background) $line');
    try {
      final preferences = await SharedPreferences.getInstance();
      await preferences.reload();
      final lines = [...?preferences.getStringList(_backgroundKey), line];
      await preferences.setStringList(
          _backgroundKey, lines.length > _max ? lines.sublist(lines.length - _max) : lines);
    } on Object {
      // Printed above; nothing more to do.
    }
  }

  /// Records which screen is on top once a navigation has had time to land, the session
  /// included: whether the route the app chose is what the customer actually sees.
  static void recordTopScreenLater(GoRouter router, String stage) {
    if (!enabled) return;
    Timer(const Duration(milliseconds: 1500), () {
      try {
        final top = router.routerDelegate.currentConfiguration.last.matchedLocation;
        record(stage, detail: 'top=${redact(top)}');
      } on Object catch (error) {
        record(stage, detail: 'unreadable ${error.runtimeType}');
      }
    });
  }

  static Future<void> clear() async {
    _lines.clear();
    changes.value++;
    try {
      final preferences = _preferences ?? await SharedPreferences.getInstance();
      await preferences.remove(_key);
      await preferences.remove(_backgroundKey);
      await preferences.remove(_nativeKey);
    } on Object {
      // Nothing to clear.
    }
  }

  /// A push's data as far as it may be written down: its keys, its kind, and whether it
  /// named a subject. No other value.
  @visibleForTesting
  static String describe(Map<String, String?> data) {
    final keys = data.entries.where((entry) => entry.value != null).map((entry) => entry.key).toList()..sort();
    final kind = data['kind'];
    final shownKind = kind == null
        ? '(none)'
        : RegExp(r'^[A-Za-z]{1,64}$').hasMatch(kind)
            ? kind
            : '(not a kind)';
    final subject = (data['subjectId'] ?? '').isEmpty ? 'no' : 'yes';
    return 'keys=[${keys.join(',')}] kind=$shownKind subject=$subject';
  }

  /// A route with every id replaced, so its SHAPE is kept and nothing it names is.
  static String redact(String? route) {
    if (route == null) return '(none)';
    return route
        .split('/')
        .map((segment) => RegExp(r'^[A-Za-z][A-Za-z-]*$').hasMatch(segment) || segment.isEmpty ? segment : ':id')
        .join('/');
  }

  static String _line(String stage, Map<String, String?>? data, String? detail) {
    final time = DateTime.now().toIso8601String();
    final clock = time.length >= 23 ? time.substring(11, 23) : time;
    return [
      clock,
      stage,
      if (data != null) describe(data),
      ?detail,
    ].join(' ');
  }

  static void _trim() {
    if (_lines.length > _max) _lines.removeRange(0, _lines.length - _max);
  }

  static Future<void> _save(String key, List<String> lines) async {
    try {
      final preferences = _preferences ?? await SharedPreferences.getInstance();
      await preferences.setStringList(key, List.of(lines));
    } on Object {
      // Printed already.
    }
  }
}
