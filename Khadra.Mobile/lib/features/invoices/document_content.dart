import '../../api/dtos.dart';

/// The version 1 reader of an issued document's stored content (payments Phase
/// 5b). Its contract is written in `docs/contracts/README.md` — the website's
/// reader follows the same one — and `financial_documents_contract_test.dart`
/// proves it against the fixture the server itself writes.
///
/// A document renders WHOLE, or not at all: a financial record shown with a line
/// quietly missing is worse than none on screen, and an installed build cannot be
/// patched once it has shown one. So this reader
///
/// - IGNORES keys it does not know, anywhere, and keeps sections and lines in the
///   order given — it never picks one out by its `key`; an absent `label` is
///   null, an absent `timeNote` or `notice` is nothing to show;
/// - FAILS CLOSED (answers null) only on a structural break: a missing title,
///   headline label or money, a section heading or its lines, a line's key; a
///   line with no value or with more than one (a key holding null is no value);
///   a text that is not two strings; a known key of another type; and a schema
///   version it does not know;
/// - DEGRADES nothing itself: an amount or a time off its pattern is still read,
///   and `Formats` prints it as it is.
///
/// Every word and figure inside a document comes from here; a screen adds no
/// label and computes nothing.
class DocumentContent {
  const DocumentContent({
    required this.title,
    required this.headlineLabel,
    required this.headlineAmount,
    required this.headlineCurrency,
    required this.sections,
    required this.timeNote,
    required this.notice,
  });

  /// The schema versions this build renders. A later one is added beside
  /// version 1, never in its place: documents are permanent.
  static const supportedSchemaVersions = <int>{1};

  final BilingualText title;
  final BilingualText headlineLabel;
  final String headlineAmount;
  final String headlineCurrency;
  final List<DocumentSection> sections;

  /// Optional: absent, nothing is shown.
  final BilingualText? timeNote;

  /// Optional: absent, nothing is shown — no client ever writes the tax-invoice
  /// sentence itself.
  final BilingualText? notice;

  /// The content of a document of [schemaVersion] (the page's
  /// `snapshotSchemaVersion`, never the snapshot's own), or null when this build
  /// cannot show it whole.
  static DocumentContent? tryParse(int schemaVersion, Object? snapshot) {
    if (!supportedSchemaVersions.contains(schemaVersion)) return null;
    try {
      final content = _object(_object(snapshot)['content']);
      final headline = _object(content['headline']);
      final money = _money(headline['money']);
      return DocumentContent(
        title: _bilingual(content['title']),
        headlineLabel: _bilingual(headline['label']),
        headlineAmount: money.$1,
        headlineCurrency: money.$2,
        sections: [for (final section in _list(content['sections'])) _section(section)],
        timeNote: _optionalBilingual(content['timeNote']),
        notice: _optionalBilingual(content['notice']),
      );
    } on _Unreadable {
      return null;
    }
  }

  static DocumentSection _section(Object? value) {
    final node = _object(value);
    return DocumentSection(
      key: _string(node['key']),
      heading: _bilingual(node['heading']),
      lines: [for (final line in _list(node['lines'])) _line(line)],
    );
  }

  static const _kinds = ['money', 'instant', 'text', 'plain'];

  static DocumentLine _line(Object? value) {
    final node = _object(value);
    final present = [for (final kind in _kinds) if (node[kind] != null) kind];
    if (present.length != 1) throw const _Unreadable();
    final kind = present.single;
    final raw = node[kind];
    final DocumentValue parsed = switch (kind) {
      'money' => MoneyValue(_money(raw).$1, _money(raw).$2),
      'instant' => InstantValue(_string(_object(raw)['utc']), _string(_object(raw)['local'])),
      'text' => TextValue(_bilingual(raw)),
      _ => PlainValue(_string(raw)),
    };
    return DocumentLine(
      key: _string(node['key']),
      label: node['label'] == null ? null : _bilingual(node['label']),
      value: parsed,
    );
  }

  static (String, String) _money(Object? value) {
    final node = _object(value);
    return (_string(node['amount']), _string(node['currency']));
  }

  static BilingualText? _optionalBilingual(Object? value) => value == null ? null : _bilingual(value);

  static BilingualText _bilingual(Object? value) {
    final node = _object(value);
    return BilingualText(_string(node['en']), _string(node['ar']));
  }

  static Map<String, dynamic> _object(Object? value) =>
      value is Map<String, dynamic> ? value : throw const _Unreadable();

  static List<dynamic> _list(Object? value) => value is List<dynamic> ? value : throw const _Unreadable();

  static String _string(Object? value) => value is String ? value : throw const _Unreadable();
}

/// One section of a document, in its stored order.
class DocumentSection {
  const DocumentSection({required this.key, required this.heading, required this.lines});

  final String key;
  final BilingualText heading;
  final List<DocumentLine> lines;
}

/// One line: a label (or none, when the value stands alone) and exactly one value.
class DocumentLine {
  const DocumentLine({required this.key, required this.label, required this.value});

  final String key;
  final BilingualText? label;
  final DocumentValue value;
}

/// The four kinds of value version 1 has. A fifth would be a new schema version.
sealed class DocumentValue {
  const DocumentValue();
}

/// An amount as stored — a string, never parsed to a double — and its currency.
final class MoneyValue extends DocumentValue {
  const MoneyValue(this.amount, this.currency);

  final String amount;
  final String currency;
}

/// An instant: `local` is the frozen Amman wall time a screen shows; `utc` is for
/// machines.
final class InstantValue extends DocumentValue {
  const InstantValue(this.utc, this.local);

  final String utc;
  final String local;
}

/// A worded text in both languages.
final class TextValue extends DocumentValue {
  const TextValue(this.text);

  final BilingualText text;
}

/// A literal as registered: a number, a reference, a plate, a name.
final class PlainValue extends DocumentValue {
  const PlainValue(this.plain);

  final String plain;
}

/// A structural break: the document is refused whole.
class _Unreadable implements Exception {
  const _Unreadable();
}
