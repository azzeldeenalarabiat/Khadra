/// A reader for Xcode's `project.pbxproj`, so the iOS project can be checked on a
/// machine that has no Xcode.
///
/// The file is an old-style (OpenStep) property list: dictionaries in braces,
/// arrays in parentheses, strings quoted or bare, and a comment beside nearly
/// every object id. This reads exactly that and nothing more; it never writes one.
/// A file it cannot read is reported with its line, which is the point: a
/// hand-edited project that would stop `xcodebuild` in its tracks stops here first.
class Pbxproj {
  Pbxproj._(this.root, this.duplicateKeys);

  factory Pbxproj.parse(String source) {
    final parser = _Parser(source);
    final root = parser.document();
    if (root is! Map<String, Object>) {
      throw const FormatException('project.pbxproj is not a dictionary');
    }
    return Pbxproj._(root, List.unmodifiable(parser.duplicateKeys));
  }

  final Map<String, Object> root;

  /// Keys that appeared twice in one dictionary. Xcode keeps the last one without
  /// a word, so a duplicated object id is a silently lost object.
  final List<String> duplicateKeys;

  Map<String, Object> get objects => root['objects']! as Map<String, Object>;

  Map<String, Object> object(String id) =>
      objects[id] as Map<String, Object>? ??
      (throw StateError('project.pbxproj names $id, which is not an object in it'));

  Map<String, Object> get project => object(root['rootObject']! as String);

  /// The native target with this name — `Runner`, `RunnerTests`.
  MapEntry<String, Map<String, Object>> target(String name) => objects.entries
      .where((e) => (e.value as Map)['isa'] == 'PBXNativeTarget' && (e.value as Map)['name'] == name)
      .map((e) => MapEntry(e.key, e.value as Map<String, Object>))
      .single;

  /// The build configurations of the project or of a target, in list order.
  List<Map<String, Object>> configurationsOf(Map<String, Object> owner) {
    final list = object(owner['buildConfigurationList']! as String);
    return [for (final id in list['buildConfigurations']! as List) object(id as String)];
  }

  /// The configuration list of the project or of a target.
  Map<String, Object> configurationListOf(Map<String, Object> owner) =>
      object(owner['buildConfigurationList']! as String);

  /// Every object id the file mentions anywhere — as a value, in a list, or as a
  /// dictionary key such as `TargetAttributes` — except the definitions themselves.
  Set<String> get referencedIds {
    final found = <String>{};
    void visit(Object value) {
      switch (value) {
        case String() when _objectId.hasMatch(value):
          found.add(value);
        case List():
          for (final item in value) {
            visit(item as Object);
          }
        case Map():
          for (final entry in value.entries) {
            visit(entry.key as Object);
            visit(entry.value as Object);
          }
      }
    }

    objects.values.forEach(visit);
    visit(root['rootObject']!);
    return found;
  }

  static final _objectId = RegExp(r'^[0-9A-F]{24}$');
}

class _Parser {
  _Parser(this._source);

  final String _source;
  int _at = 0;
  final duplicateKeys = <String>[];

  Object document() {
    final value = _value();
    _skip();
    if (_at != _source.length) _fail('text after the end of the project');
    return value;
  }

  Object _value() {
    _skip();
    if (_at >= _source.length) _fail('the file ends in the middle of a value');
    return switch (_source[_at]) {
      '{' => _dictionary(),
      '(' => _array(),
      '"' => _quoted(),
      _ => _bare(),
    };
  }

  Map<String, Object> _dictionary() {
    _at++;
    final map = <String, Object>{};
    while (true) {
      _skip();
      if (_at >= _source.length) _fail('a dictionary is never closed');
      if (_source[_at] == '}') {
        _at++;
        return map;
      }
      final key = _value();
      if (key is! String) _fail('a dictionary key must be a string');
      _expect('=');
      final value = _value();
      _expect(';');
      if (map.containsKey(key)) duplicateKeys.add(key);
      map[key] = value;
    }
  }

  List<Object> _array() {
    _at++;
    final list = <Object>[];
    while (true) {
      _skip();
      if (_at >= _source.length) _fail('an array is never closed');
      if (_source[_at] == ')') {
        _at++;
        return list;
      }
      list.add(_value());
      _skip();
      if (_at < _source.length && _source[_at] == ',') {
        _at++;
      } else if (_at >= _source.length || _source[_at] != ')') {
        _fail('an array item is not followed by "," or ")"');
      }
    }
  }

  String _quoted() {
    _at++;
    final out = StringBuffer();
    while (true) {
      if (_at >= _source.length) _fail('a string is never closed');
      final char = _source[_at++];
      if (char == '"') return out.toString();
      if (char != r'\') {
        out.write(char);
        continue;
      }
      if (_at >= _source.length) _fail('a string ends in an escape');
      final escaped = _source[_at++];
      out.write(switch (escaped) {
        'n' => '\n',
        't' => '\t',
        'r' => '\r',
        '"' || r'\' || "'" => escaped,
        _ => _fail('unknown escape \\$escaped'),
      });
    }
  }

  String _bare() {
    final start = _at;
    while (_at < _source.length && _isBare(_source.codeUnitAt(_at))) {
      _at++;
    }
    if (_at == start) _fail('unexpected "${_source[_at]}"');
    return _source.substring(start, _at);
  }

  static bool _isBare(int c) =>
      (c >= 0x30 && c <= 0x39) || // 0-9
      (c >= 0x41 && c <= 0x5A) || // A-Z
      (c >= 0x61 && c <= 0x7A) || // a-z
      r'_$+/:.-'.codeUnits.contains(c);

  void _expect(String char) {
    _skip();
    if (_at >= _source.length || _source[_at] != char) _fail('expected "$char"');
    _at++;
  }

  /// Whitespace and both comment forms, which carry no meaning in this format.
  void _skip() {
    while (_at < _source.length) {
      final char = _source[_at];
      if (char == ' ' || char == '\t' || char == '\n' || char == '\r') {
        _at++;
      } else if (_source.startsWith('//', _at)) {
        final end = _source.indexOf('\n', _at);
        _at = end < 0 ? _source.length : end + 1;
      } else if (_source.startsWith('/*', _at)) {
        final end = _source.indexOf('*/', _at + 2);
        if (end < 0) _fail('a comment is never closed');
        _at = end + 2;
      } else {
        return;
      }
    }
  }

  Never _fail(String problem) {
    final line = '\n'.allMatches(_source.substring(0, _at.clamp(0, _source.length))).length + 1;
    throw FormatException('project.pbxproj, line $line: $problem');
  }
}
