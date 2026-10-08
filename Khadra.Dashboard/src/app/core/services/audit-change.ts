import { EN, TranslationKey } from '../i18n/en';
import { EnumFamily, StatusScope, enumKey, spellEnumName, statusKey } from '../i18n/status-key';
import { roleLabel } from '../models/user-display';
import { Translate } from './dashboard.presenter';

/**
 * The audit log's Change column, in the reader's language (pre-launch items 50 and 174).
 *
 * `previousValue` and `newValue` are kept for ever in a table that refuses UPDATE, so what a row holds is decided by
 * when it was written, and this reads each kind for what it is — never by translating English it finds:
 *
 * - **A machine name** — a status, a role, a reason code. Worded through the same dictionary as every other status,
 *   chosen by the entry's record type, because `Approved` on a dealership and on a booking are different words.
 * - **Parts** — a JSON object of facts, which the server writes from 2026-10-08 where it used to compose an English
 *   sentence: a dispute decision's figures, a handover's outcome, a city's two names. Composed here, per action.
 * - **Anything else** is shown exactly as recorded, in a left-to-right run: a sentence written before 2026-10-08, a
 *   document number, a stored amount, a version label. An old sentence is history; guessing at it would not be.
 */
export interface AuditChangeFacts {
  readonly action: string;
  readonly entityType: string;
  readonly previousValue: string | null;
  readonly newValue: string | null;
}

export type AuditValueText =
  /** In the reader's language. */
  | { readonly kind: 'worded'; readonly text: string }
  /** Exactly as it was recorded. */
  | { readonly kind: 'recorded'; readonly text: string };

export interface AuditValueWords {
  readonly t: Translate;
  readonly arabic: boolean;
  /** An amount as it was STORED ("18.000"), with its currency — never through a float. */
  readonly storedMoney: (amount: string, currency: string) => string;
}

export function auditValueText(
  entry: AuditChangeFacts,
  side: 'previous' | 'new',
  words: AuditValueWords,
): AuditValueText | null {
  const value = side === 'previous' ? entry.previousValue : entry.newValue;
  if (value === null || value === undefined || value === '') return null;
  const parts = partsOf(value);
  const worded = parts ? fromParts(entry.action, parts, words) : fromName(entry, value, words.t);
  return worded === null ? { kind: 'recorded', text: value } : { kind: 'worded', text: worded };
}

type Parts = Readonly<Record<string, unknown>>;

/** A JSON object, or nothing: a value that only looks like one is recorded text. */
function partsOf(value: string): Parts | null {
  if (!value.startsWith('{')) return null;
  try {
    const parsed: unknown = JSON.parse(value);
    return parsed !== null && typeof parsed === 'object' && !Array.isArray(parsed)
      ? (parsed as Parts)
      : null;
  } catch {
    return null;
  }
}

const text = (parts: Parts, key: string): string | null => {
  const value = parts[key];
  return typeof value === 'string' && value !== '' ? value : null;
};

function fromParts(action: string, parts: Parts, words: AuditValueWords): string | null {
  const { t } = words;
  switch (action) {
    case 'DisputeResolved': {
      const currency = text(parts, 'currency');
      const held = text(parts, 'held');
      const refund = text(parts, 'refund');
      const platform = text(parts, 'platform');
      const dealer = text(parts, 'dealer');
      if (!currency || !held || !refund || !platform || !dealer) return null;
      const money = (amount: string) => words.storedMoney(amount, currency);
      const split = {
        held: money(held),
        refund: money(refund),
        platform: money(platform),
        dealer: money(dealer),
      };
      const charge = text(parts, 'charge');
      return charge
        ? t('auditLog.change.disputeResolvedWithCharge', {
            ...split,
            charge: words.storedMoney(charge, text(parts, 'chargeCurrency') ?? currency),
          })
        : t('auditLog.change.disputeResolved', split);
    }
    case 'HandoverVerified':
    case 'HandoverUnverified': {
      const status = text(parts, 'status');
      const handover = text(parts, 'handover');
      const method = text(parts, 'method');
      if (!status || !handover || !method) return null;
      return t('auditLog.change.handover', {
        status: statusWord(status, 'booking', t),
        handover: enumWord('handoverType', handover, t),
        method: enumWord('handoverVerification', method, t),
      });
    }
    case 'HandoverCodeLocked': {
      const handover = text(parts, 'handover');
      const tries = parts['wrongTries'];
      if (!handover || typeof tries !== 'number') return null;
      return t('auditLog.change.codeLocked', {
        handover: enumWord('handoverType', handover, t),
        count: tries,
      });
    }
    case 'LookupCreated':
    case 'LookupRenamed':
    case 'LookupRetired':
    case 'LookupRestored': {
      const en = text(parts, 'en');
      const ar = text(parts, 'ar');
      const offered = parts['offered'];
      if ((!en && !ar) || typeof offered !== 'boolean') return null;
      return t('auditLog.change.lookup', {
        name: (words.arabic ? (ar ?? en) : (en ?? ar)) ?? '',
        state: t(offered ? 'lookups.stateOffered' : 'lookups.stateRetired'),
      });
    }
    default:
      return null;
  }
}

/** A machine name, worded by the record it belongs to; null when it is not one this build knows to be a name. */
function fromName(entry: AuditChangeFacts, value: string, t: Translate): string | null {
  switch (entry.entityType) {
    case 'Dealer':
    case 'Dispute':
      return known(statusKey(value), t);
    case 'Booking':
      return known(statusKey(value, 'booking'), t);
    case 'Customer':
      return entry.action === 'CustomerDocumentRejected'
        ? customerDocument(value, t)
        : known(statusKey(value), t);
    case 'AdminUser':
      return entry.action === 'AdminInvited' || entry.action === 'AdminInvitationResent'
        ? roleLabel(value, t) || null
        : known(statusKey(value), t);
    case 'Review':
      return known(enumKey('reviewHideReason', value), t);
    case 'OfficePayable':
      return known(enumKey('payableHoldReason', value), t);
    case 'PaymentIncident':
      return known(enumKey('paymentIncidentKind', value), t);
    default:
      return null;
  }
}

/** "DrivingLicenceFront:Rejected" — the slot the file filled, and where it stood. Both codes, or recorded text. */
function customerDocument(value: string, t: Translate): string | null {
  const [type, status, ...rest] = value.split(':');
  if (!type || !status || rest.length > 0) return null;
  const typeKey = `renterDocs.type.${type.charAt(0).toLowerCase()}${type.slice(1)}`;
  const statusName = known(statusKey(status, 'customerDocument'), t);
  if (!(typeKey in EN) || statusName === null) return null;
  return t('auditLog.change.customerDocument', {
    type: t(typeKey as TranslationKey),
    status: statusName,
  });
}

const known = (key: TranslationKey | null, t: Translate): string | null =>
  key === null ? null : t(key);

/** A status inside composed parts: worded, or spelled out from its name rather than dropped. */
const statusWord = (name: string, scope: StatusScope, t: Translate): string =>
  known(statusKey(name, scope), t) ?? spellEnumName(name);

const enumWord = (family: EnumFamily, name: string, t: Translate): string =>
  known(enumKey(family, name), t) ?? spellEnumName(name);
