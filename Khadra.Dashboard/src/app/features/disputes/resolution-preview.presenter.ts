import { KeyValue, Tone } from '../../core/models/console.models';
import { OfficeExpectedOutcome, ResolutionPreview } from '../../core/models/disputes.api';
import { PayableLineRow, PayoutFormat, PayoutWords, lineRow, netText } from '../payouts/payouts.presenter';

/**
 * What a dispute decision does to the money, worded for whoever reads it (Wave 2 C1; E2E F37). Every figure is the
 * server's: the preview and the office's expected outcome come from the one office function the payouts ledger uses.
 *
 * The words say what the RECORDS will say, never what money will do. The customer's refund is "requested" (the sweep
 * sends it). The office's net is "as the payouts ledger will record it" (settlement is by hand, and can be held). A
 * cancellation's figures hold "if nothing else is decided" before its dispute window closes.
 */

export interface PreviewWords extends PayoutWords {
  /** `I18nService.statusLabel` for a booking status. */
  readonly status: (name: string) => string;
}

export interface PreviewView {
  readonly statusLine: string;
  readonly rows: readonly KeyValue[];
  readonly lines: readonly PayableLineRow[];
  readonly net: { readonly text: string; readonly tone: Tone } | null;
  readonly notes: readonly string[];
}

/** The administrator's preview, beneath the amounts and again in the confirmation. */
export function previewView(preview: ResolutionPreview, words: PreviewWords, format: PayoutFormat): PreviewView {
  const t = words.t;
  const rows: KeyValue[] = [
    { k: t('disputePreview.refundRequested'), v: format.money(preview.customer.refundRequested) },
    { k: t('disputePreview.keptByKhadra'), v: format.money(preview.platform.retainedShare) },
  ];
  const notes: string[] = [];

  if (preview.officeState !== 'Final') {
    notes.push(t('disputePreview.notApplicable'));
    return { statusLine: statusLine(preview, words), rows, lines: [], net: null, notes };
  }

  // The office's part is the payable's own lines, as the payouts page shows them: its money, less the commission
  // capped at it, less any charge.
  notes.push(
    t('disputePreview.commissionCapped', {
      frozen: format.money(preview.office.frozenCommission),
      money: format.money(preview.office.money),
    }),
  );
  if (preview.earlierDecisions) notes.push(t('disputePreview.earlier', { count: preview.earlierDecisions.count }));
  notes.push(...timing(preview.recordedNotBefore, preview.furtherDecisionsPossibleUntil, words, format));
  notes.push(t('disputePreview.settledByHand'));

  return {
    statusLine: statusLine(preview, words),
    rows,
    lines: preview.lines.map((line) => lineRow(line, words, format)),
    net: netText(preview.office.net, 'admin', words, format),
    notes,
  };
}

/** One sentence for the confirmation dialog: what the ledger will record, and until when it may still change. */
export function previewConfirmSentence(preview: ResolutionPreview, words: PreviewWords, format: PayoutFormat): string {
  const t = words.t;
  if (preview.officeState !== 'Final') return t('disputePreview.notApplicable');
  const net = netText(preview.office.net, 'admin', words, format).text;
  return preview.furtherDecisionsPossibleUntil
    ? t('disputePreview.confirmUntil', { net, when: format.dateTime(preview.furtherDecisionsPossibleUntil) })
    : t('disputePreview.confirm', { net });
}

/** The office's own reading of a decided dispute: the recorded payable, or the projection until there is one. */
export function officeOutcomeView(outcome: OfficeExpectedOutcome, words: PayoutWords, format: PayoutFormat): PreviewView {
  const t = words.t;
  const notes: string[] = [
    t(outcome.source === 'Recorded' ? 'officeOutcome.recorded' : 'officeOutcome.projected'),
  ];
  if (outcome.furtherDecisionsPossibleUntil) {
    notes.push(t('disputePreview.untilWindowOffice', { when: format.dateTime(outcome.furtherDecisionsPossibleUntil) }));
  }
  return {
    statusLine: '',
    rows: [],
    lines: outcome.lines.map((line) => lineRow(line, words, format)),
    net: netText(outcome.net, 'office', words, format),
    notes,
  };
}

function statusLine(preview: ResolutionPreview, words: PreviewWords): string {
  return words.t('disputePreview.statusAfter', { status: words.status(preview.statusAfter) });
}

function timing(notBefore: string, until: string | null, words: PreviewWords, format: PayoutFormat): string[] {
  const lines = [words.t('disputePreview.recordedNotBefore', { when: format.dateTime(notBefore) })];
  if (until) lines.push(words.t('disputePreview.untilWindow', { when: format.dateTime(until) }));
  return lines;
}
