import '../../api/dtos.dart';
import '../../core/format/formats.dart';
import '../../l10n/app_localizations.dart';
import 'document_content.dart';

/// Issued financial documents in words (payments Phase 5b). Two kinds of text
/// meet here, and they come from different places on purpose:
///
/// - what is INSIDE a document — every label, heading, figure and time — is the
///   stored document itself, read in the app's language; nothing is worded or
///   computed here;
/// - what SURROUNDS it — its standing, the void notice, its versions, "being
///   prepared" — is a live fact the server sends beside the document, worded from
///   the app's own strings like any other live screen.
///
/// The website words the same facts the same way (`invoice-presentation.ts`), so
/// a customer reads one account of a document on either.

enum StandingTone { neutral, bad, ok }

class StandingView {
  const StandingView(this.label, this.tone, {this.unknown = false});

  final String label;
  final StandingTone tone;

  /// A standing this build does not know: the server's own name, isolated.
  final bool unknown;
}

/// The chip beside a document. None for a current one. A standing this build does
/// not know is shown as the server named it rather than left out: saying nothing
/// about a document's standing would present it as current.
StandingView? standingOf(String status, AppLocalizations l10n) => switch (status) {
      'Current' => null,
      'Superseded' => StandingView(l10n.invoicesStatusSuperseded, StandingTone.neutral),
      'Voided' => StandingView(l10n.invoicesStatusVoided, StandingTone.bad),
      _ => StandingView(status, StandingTone.neutral, unknown: true),
    };

// ── Lists ─────────────────────────────────────────────────────────────────────

class InvoiceRowView {
  const InvoiceRowView({
    required this.id,
    required this.title,
    required this.number,
    required this.version,
    required this.standing,
    required this.booking,
    required this.issued,
    required this.headlineLabel,
    required this.headline,
  });

  final String id;
  final String title;
  final String number;

  /// "Version 2" above the first, else null.
  final String? version;
  final StandingView? standing;

  /// "Booking KH-…", the reference isolated inside the sentence.
  final String booking;
  final String issued;
  final String headlineLabel;
  final String headline;
}

/// A number or a reference inside a sentence: isolated, so a Latin run inside an
/// Arabic sentence keeps its place — the website's messages isolate every
/// parameter the same way.
String _run(String text) => Formats.isolate(text);

/// A row is a live screen: its figure arrives as a plain number, so it goes
/// through the live formatter.
InvoiceRowView invoiceRow(FinancialDocumentRow row, AppLocalizations l10n, Formats formats) {
  final arabic = formats.isArabic;
  final issued = row.issuedAt;
  return InvoiceRowView(
    id: row.documentId,
    title: row.title.of(arabic: arabic),
    number: row.number,
    version: row.version > 1 ? l10n.invoicesVersion(row.version) : null,
    standing: standingOf(row.status, l10n),
    booking: l10n.invoicesBooking(_run(row.bookingReference)),
    issued: issued == null ? '' : l10n.invoicesIssued(formats.longDate(issued)),
    headlineLabel: row.headlineLabel.of(arabic: arabic),
    headline: formats.money(row.headline),
  );
}

class PreparingView {
  const PreparingView({required this.key, required this.label, required this.date});

  final String key;
  final String label;

  /// The date of the money it will record, so two payments give two rows a reader
  /// can tell apart.
  final String date;
}

PreparingView preparingRow(PendingFinancialDocument pending, AppLocalizations l10n, Formats formats) {
  final occurred = pending.occurredAt;
  return PreparingView(
    key: '${pending.type}:${pending.subjectId}',
    label: switch (pending.type) {
      FinancialDocumentTypes.paymentReceipt => l10n.invoicesPreparingPaymentReceipt,
      FinancialDocumentTypes.refundReceipt => l10n.invoicesPreparingRefundReceipt,
      FinancialDocumentTypes.bookingStatement => l10n.invoicesPreparingBookingStatement,
      _ => l10n.invoicesPreparingOther,
    },
    date: occurred == null ? '' : formats.longDate(occurred),
  );
}

// ── One document ──────────────────────────────────────────────────────────────

/// How a literal the document stored as registered (a `plain` value) is kept apart
/// from the text around it — the same rule as the website's, in
/// docs/contracts/README.md:
///
/// - [ltr]: Latin — a number, a reference, a plate, an e-mail, a phone — left to
///   right whatever surrounds it: "+962 6 000 0000" has no strong character, and
///   left to find its own direction on an Arabic screen its groups reorder;
/// - [auto]: anything written with an Arabic (or Hebrew) letter — a name as
///   registered — which takes its own direction, so a mixed name keeps its halves
///   and its punctuation in order.
enum LiteralDirection { ltr, auto }

final _rtlLetter = RegExp('[\u0590-\u08FF\uFB1D-\uFDFF\uFE70-\uFEFF]');

/// Null for every kind of value that is not a literal: the stored text or the
/// formatter has already isolated it.
LiteralDirection? directionOf(DocumentValue value) => switch (value) {
      PlainValue(:final plain) => _rtlLetter.hasMatch(plain) ? LiteralDirection.auto : LiteralDirection.ltr,
      _ => null,
    };

class DocumentLineView {
  const DocumentLineView({required this.key, required this.label, required this.value, required this.direction});

  final String key;
  final String label;
  final String value;
  final LiteralDirection? direction;
}

/// A section's lines in their stored order: labelled lines together, and a line
/// with no label — a value that stands alone, such as "Paid in full online" — as
/// a paragraph of its own.
sealed class DocumentBlock {
  const DocumentBlock(this.key);

  final String key;
}

final class LinesBlock extends DocumentBlock {
  const LinesBlock(super.key, this.lines);

  final List<DocumentLineView> lines;
}

final class AloneBlock extends DocumentBlock {
  const AloneBlock(super.key, this.text, this.direction);

  final String text;
  final LiteralDirection? direction;
}

class DocumentSectionView {
  const DocumentSectionView({required this.key, required this.heading, required this.blocks});

  final String key;
  final String heading;
  final List<DocumentBlock> blocks;
}

class DocumentBodyView {
  const DocumentBodyView({
    required this.title,
    required this.headlineLabel,
    required this.headline,
    required this.sections,
    required this.timeNote,
    required this.notice,
  });

  final String title;
  final String headlineLabel;
  final String headline;
  final List<DocumentSectionView> sections;
  final String? timeNote;
  final String? notice;
}

/// The stored document, in the app's language, exactly as it was issued.
DocumentBodyView documentBody(DocumentContent content, Formats formats) {
  final arabic = formats.isArabic;
  return DocumentBodyView(
    title: content.title.of(arabic: arabic),
    headlineLabel: content.headlineLabel.of(arabic: arabic),
    headline: formats.storedMoney(content.headlineAmount, content.headlineCurrency),
    sections: [
      for (final (index, section) in content.sections.indexed)
        DocumentSectionView(
          key: '$index:${section.key}',
          heading: section.heading.of(arabic: arabic),
          blocks: _blocksOf(section.lines, formats),
        ),
    ],
    timeNote: content.timeNote?.of(arabic: arabic),
    notice: content.notice?.of(arabic: arabic),
  );
}

List<DocumentBlock> _blocksOf(List<DocumentLine> lines, Formats formats) {
  final blocks = <DocumentBlock>[];
  for (final (position, line) in lines.indexed) {
    final key = '$position:${line.key}';
    final value = _valueText(line.value, formats);
    final direction = directionOf(line.value);
    final label = line.label;
    if (label == null) {
      blocks.add(AloneBlock(key, value, direction));
      continue;
    }
    final view = DocumentLineView(key: key, label: label.of(arabic: formats.isArabic), value: value, direction: direction);
    final last = blocks.isEmpty ? null : blocks.last;
    if (last is LinesBlock) {
      blocks[blocks.length - 1] = LinesBlock(last.key, [...last.lines, view]);
    } else {
      blocks.add(LinesBlock(key, [view]));
    }
  }
  return blocks;
}

String _valueText(DocumentValue value, Formats formats) => switch (value) {
      MoneyValue(:final amount, :final currency) => formats.storedMoney(amount, currency),
      InstantValue(:final local) => formats.frozenTime(local),
      TextValue(:final text) => text.of(arabic: formats.isArabic),
      PlainValue(:final plain) => plain,
    };

/// A link worded as a sentence, the document it opens, and that document's
/// number, for a screen that links the number itself.
class LinkText {
  const LinkText(this.id, this.text, this.number);

  final String id;

  /// The whole sentence, the number isolated inside it.
  final String text;
  final String number;

  /// The words before the number and after it, so a screen can keep the number
  /// in ONE piece: a sentence left to wrap on its own breaks at a hyphen inside
  /// the number ("TEST-" / "PAY-2026-000005") on a phone. Null when the number is
  /// not in the sentence, and the sentence is then shown as it is.
  ({String before, String after})? get aroundNumber {
    final run = _run(number);
    final at = text.indexOf(run);
    if (at < 0) return null;
    return (before: text.substring(0, at).trim(), after: text.substring(at + run.length).trim());
  }
}

class LinkView {
  const LinkView({required this.id, required this.number, required this.detail, required this.standing, required this.here});

  final String id;
  final String number;
  final String detail;
  final StandingView? standing;

  /// The document on screen: listed, not linked.
  final bool here;
}

class VoidView {
  const VoidView(this.text, this.replacement);

  final String text;
  final LinkText? replacement;
}

class InvoicePageView {
  const InvoicePageView({
    required this.title,
    required this.number,
    required this.standing,
    required this.versionOf,
    required this.bookingId,
    required this.booking,
    required this.voided,
    required this.newer,
    required this.body,
    required this.headlineLabel,
    required this.headline,
    required this.issued,
    required this.versions,
    required this.paymentReceipt,
    required this.refundReceipts,
    this.pdf = const PdfView(opens: [], preparing: false),
  });

  final String title;
  final String number;
  final StandingView? standing;
  final String versionOf;
  final String bookingId;
  final String booking;

  /// "Voided on …" — and, as a link to the correction, "Replaced by …".
  final VoidView? voided;

  /// On an earlier version: the newest version, linked.
  final LinkText? newer;

  /// The document itself, or null when this build cannot show it whole.
  final DocumentBodyView? body;

  /// What the screen still states when it cannot show the document.
  final String headlineLabel;
  final String headline;
  final String issued;
  final List<LinkView> versions;

  /// On a refund receipt: the payment receipt it was issued against.
  final LinkText? paymentReceipt;
  final List<LinkView> refundReceipts;

  /// Its PDFs (payments Phase 6), and whether one is still being drawn.
  final PdfView pdf;
}

/// One PDF to open: the language it is in, its button, and the name of the file
/// the platform viewer is handed — the document's number and the language, so a
/// file shared onwards says what it is.
class PdfOpenView {
  const PdfOpenView({required this.language, required this.label, required this.semantics, required this.fileStem});

  final String language;
  final String label;
  final String semantics;
  final String fileStem;
}

class PdfView {
  const PdfView({required this.opens, required this.preparing});

  final List<PdfOpenView> opens;
  final bool preparing;
}

/// The PDFs the server says were drawn, in the order it sent them (English
/// first). A language this build has never heard of is not offered.
PdfView pdfView(FinancialDocumentPage page, AppLocalizations l10n) => PdfView(
      opens: [
        for (final language in page.pdf.languages)
          if (switch (language) {
            'en' => (l10n.invoicesPdfEnglish, l10n.invoicesPdfOpenEnglish),
            'ar' => (l10n.invoicesPdfArabic, l10n.invoicesPdfOpenArabic),
            _ => null,
          }
              case (final label, final semantics))
            PdfOpenView(
              language: language,
              label: label,
              semantics: semantics,
              fileStem: '${page.row.number}-$language',
            ),
      ],
      preparing: page.pdf.preparing,
    );

InvoicePageView invoicePage(FinancialDocumentPage page, AppLocalizations l10n, Formats formats) {
  final row = page.row;
  final content = DocumentContent.tryParse(page.snapshotSchemaVersion, page.snapshot);
  final versions = [...page.links.versions]..sort((a, b) => a.version.compareTo(b.version));
  // The newest is the highest member of the family — always current, because a
  // void always carries its correction. Never `nextVersion`: that is only the
  // next member, and it can be a voided one.
  final newest = versions.isEmpty ? null : versions.last;
  final voided = page.voided;
  final replacement = voided?.replacedBy;
  final voidedAt = voided?.voidedAt;
  final issuedAt = row.issuedAt;

  return InvoicePageView(
    title: row.title.of(arabic: formats.isArabic),
    number: row.number,
    standing: standingOf(row.status, l10n),
    versionOf: l10n.invoicesVersionOf(row.version, versions.length > row.version ? versions.length : row.version),
    bookingId: row.bookingId,
    booking: l10n.invoicesBooking(_run(row.bookingReference)),
    voided: voided == null
        ? null
        : VoidView(
            // The server always dates a void. A page that arrives without the
            // date still says what happened, in the standing's own word, rather
            // than "Voided on ."
            switch (voidedAt) {
              null => l10n.invoicesStatusVoided,
              final at when replacement == null => l10n.invoicesVoidedOn(formats.longDate(at)),
              final at => l10n.invoicesVoidedOnAnd(formats.longDate(at)),
            },
            replacement == null
                ? null
                : LinkText(replacement.documentId, l10n.invoicesReplacedBy(_run(replacement.number)), replacement.number),
          ),
    newer: row.status == 'Superseded' && newest != null && newest.documentId != row.documentId
        ? LinkText(newest.documentId, l10n.invoicesNewerVersion(_run(newest.number)), newest.number)
        : null,
    body: content == null ? null : documentBody(content, formats),
    headlineLabel: row.headlineLabel.of(arabic: formats.isArabic),
    headline: formats.money(row.headline),
    issued: issuedAt == null ? '' : l10n.invoicesIssued(formats.longDate(issuedAt)),
    versions: versions.length > 1
        ? [for (final link in versions) _linkView(link, row.documentId, l10n, asVersion: true)]
        : const [],
    paymentReceipt: switch (page.links.paymentReceipt) {
      null => null,
      final receipt => LinkText(receipt.documentId, l10n.invoicesIssuedAgainst(_run(receipt.number)), receipt.number),
    },
    refundReceipts: [for (final link in page.links.refundReceipts) _linkView(link, row.documentId, l10n, asVersion: false)],
    pdf: pdfView(page, l10n),
  );
}

LinkView _linkView(FinancialDocumentLink link, String here, AppLocalizations l10n, {required bool asVersion}) => LinkView(
      id: link.documentId,
      number: link.number,
      detail: asVersion ? l10n.invoicesVersion(link.version) : '',
      standing: asVersion && link.status == 'Current'
          ? StandingView(l10n.invoicesStatusCurrent, StandingTone.ok)
          : standingOf(link.status, l10n),
      here: link.documentId == here,
    );
