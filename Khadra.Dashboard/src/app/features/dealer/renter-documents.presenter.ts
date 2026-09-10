import { RenterDocument, RenterDocumentType, RenterDocuments } from '../../core/models/bookings.api';
import { Tone } from '../../core/models/console.models';
import { TranslationKey } from '../../core/i18n/en';
import { MessageParams } from '../../core/i18n/language';

/** Passed in rather than injected: these are pure functions, and their spec calls them directly. */
export type Translate = (key: TranslationKey, params?: MessageParams) => string;

/** One of the renter's documents, ready to render. */
export interface RenterDocumentTile {
  readonly documentId: string;
  /** "Driving licence — front", in the reader's language. */
  readonly label: string;
  /** The platform's own review status, translated. `PendingReview` for everything today. */
  readonly status: string;
  readonly tone: Tone;
  /** The format the server will actually serve it as. Never guessed from the type. */
  readonly format: string;
  /** When the renter uploaded it, so a gallery can see a re-photographed document changed. */
  readonly uploadedAt: string;
  /** Where the bytes come from. Built from the two ids the console was given, and nothing else. */
  readonly href: string;
  /** Whether it can be shown in place, or only opened. A PDF is not an `&lt;img&gt;`. */
  readonly isImage: boolean;
  /** Part of the licence check spec 5.1 asks the gallery to perform. */
  readonly isLicence: boolean;
}

/**
 * What the renter-documents panel shows, as one value.
 *
 * A discriminated union rather than four booleans, because three of these are NOT errors and reading
 * them off `error()` is how a screen ends up telling a gallery the platform is broken when the
 * platform is working exactly as designed.
 */
export type RenterDocumentsPanel =
  | { readonly kind: 'loading' }
  /**
   * The access rule, not a failure: the booking is no longer live, so this gallery's window has
   * closed. The server says so with 409 `booking.renter_documents_not_available`.
   */
  | { readonly kind: 'closed'; readonly note: string }
  /** Something actually went wrong, and the gallery should try again. */
  | { readonly kind: 'failed'; readonly note: string }
  | {
      readonly kind: 'ready';
      readonly tiles: readonly RenterDocumentTile[];
      /** A sentence naming what the renter has not filed, or null when nothing is outstanding. */
      readonly missing: string | null;
      readonly isComplete: boolean;
    };

/**
 * The one refusal that means "your window has closed" rather than "something went wrong".
 *
 * Keyed on the server's stable CODE, not on the bare 409. A status code is a category and this is a
 * specific answer: any future conflict on these routes would otherwise be worded to a gallery as
 * "your access has expired", which would be a lie about why they cannot see the licence. The detail
 * screen's own `describe()` keys on codes for the same reason.
 */
const ACCESS_CLOSED = 'booking.renter_documents_not_available';

const TYPE_LABELS: Readonly<Record<RenterDocumentType, TranslationKey>> = {
  DrivingLicenceFront: 'renterDocs.type.drivingLicenceFront',
  DrivingLicenceBack: 'renterDocs.type.drivingLicenceBack',
  NationalId: 'renterDocs.type.nationalId',
  Passport: 'renterDocs.type.passport',
};

const FORMAT_LABELS: Readonly<Record<string, TranslationKey>> = {
  'application/pdf': 'docFormat.pdf',
  'image/jpeg': 'docFormat.jpeg',
  'image/png': 'docFormat.png',
  'image/webp': 'docFormat.webp',
};

/**
 * Decides what the panel is showing, from the resource's own three-valued state.
 *
 * @param status the httpResource status; only 'error' and 'resolved' decide anything here.
 * @param value the server's answer, when there is one. Read through `loaded()`, never `value()`.
 * @param code the ProblemDetails `code` of the failure, when there was one.
 */
export function toRenterDocumentsPanel(
  status: string,
  value: RenterDocuments | undefined,
  code: string | undefined,
  bookingId: string,
  href: (bookingId: string, documentId: string) => string,
  t: Translate,
  formatDate: (iso: string) => string,
): RenterDocumentsPanel {
  if (status === 'error') {
    // One code is the rule working. Everything else -- a dropped connection, a 500, a bodiless 403
    // for a role that is not dealer staff -- is a failure the gallery can retry, and must not be
    // worded as though their access had expired.
    return code === ACCESS_CLOSED
      ? { kind: 'closed', note: t('renterDocs.closed') }
      : { kind: 'failed', note: t('renterDocs.failed') };
  }

  if (!value) return { kind: 'loading' };

  return {
    kind: 'ready',
    tiles: value.documents.map((document) => toTile(document, bookingId, href, t, formatDate)),
    missing: missingSentence(value.missing, t),
    isComplete: value.isComplete,
  };
}

function toTile(
  document: RenterDocument,
  bookingId: string,
  href: (bookingId: string, documentId: string) => string,
  t: Translate,
  formatDate: (iso: string) => string,
): RenterDocumentTile {
  const typeKey = TYPE_LABELS[document.type];
  return {
    documentId: document.documentId,
    // An unknown type falls back to the server's own word rather than to a blank tile: a gallery
    // deciding whether to hand over a car is better served by "InternationalPermit" than by nothing.
    label: typeKey ? t(typeKey) : document.type,
    status: document.status,
    tone: statusTone(document.status),
    format: formatLabel(document.contentType, t),
    uploadedAt: formatDate(document.uploadedAt),
    href: href(bookingId, document.documentId),
    isImage: document.contentType.startsWith('image/'),
    isLicence:
      document.type === 'DrivingLicenceFront' || document.type === 'DrivingLicenceBack',
  };
}

/**
 * What a review status looks like.
 *
 * Everything is `PendingReview` today, and that is honest rather than reassuring on purpose: nothing
 * on the platform moves a customer document out of it (pre-launch item 63), so a green tick here
 * would be the screen inventing a verification that never happened.
 */
function statusTone(status: string): Tone {
  if (status === 'Verified') return 'ok';
  if (status === 'Rejected') return 'bad';
  return 'warn';
}

function formatLabel(contentType: string, t: Translate): string {
  const key = FORMAT_LABELS[contentType];
  // The raw type rather than a guess, for anything the platform starts accepting later.
  return key ? t(key) : contentType;
}

/**
 * "Not on file: driving licence — back, national ID", or null when nothing is outstanding.
 *
 * The SERVER decides which documents are missing — it is the customer's record that knows whether
 * they are a foreign national, and therefore whether a passport or a national ID is owed. This only
 * turns that list into a sentence.
 */
function missingSentence(missing: readonly RenterDocumentType[], t: Translate): string | null {
  if (missing.length === 0) return null;

  const names = missing.map((type) => {
    const key = TYPE_LABELS[type];
    return key ? t(key) : type;
  });
  return t('renterDocs.notOnFile', { documents: names.join(t('common.listSeparator')) });
}
