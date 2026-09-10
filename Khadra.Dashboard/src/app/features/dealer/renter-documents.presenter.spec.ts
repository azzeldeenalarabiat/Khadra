import { describe, expect, it } from 'vitest';
import {
  DealerDocumentReview,
  RenterDocument,
  RenterDocuments,
} from '../../core/models/bookings.api';
import { toRenterDocumentsPanel, Translate } from './renter-documents.presenter';
import { EN } from '../../core/i18n/en';
import { AR } from '../../core/i18n/ar';
import { resolveMessage } from '../../core/i18n/resolve';

/**
 * The gallery's view of a renter's identity papers (spec 5.1, pre-launch item 63).
 *
 * Resolves the REAL dictionaries, so these assertions read as the words a gallery sees rather than
 * as key names — and so the Arabic pass below proves the panel is translated rather than proving
 * that a lookup was called.
 */
const en: Translate = (key, params) => resolveMessage(EN[key], params, 'en-GB', false) ?? key;
const ar: Translate = (key, params) =>
  resolveMessage(AR[key], params, 'ar-JO-u-nu-latn', true) ?? key;

const BOOKING = '01a08222-0228-7f9f-b051-fb28aca2ac4c';
const DOCUMENT = '01a08222-65c0-7bba-933e-74274446bb9e';

/** The real URL builder from DealerBookingsService, so the shape under test is the shipped one. */
const href = (bookingId: string, documentId: string) =>
  `/api/v1/bookings/${encodeURIComponent(bookingId)}/renter-documents/${encodeURIComponent(documentId)}`;

const date = (iso: string) => `formatted:${iso}`;

const document = (over: Partial<RenterDocument> = {}): RenterDocument => ({
  documentId: DOCUMENT,
  type: 'DrivingLicenceFront',
  dealerReview: null,
  contentType: 'image/jpeg',
  uploadedAt: '2026-09-01T09:00:00Z',
  ...over,
});

const answer = (over: Partial<RenterDocuments> = {}): RenterDocuments => ({
  documents: [document()],
  isComplete: false,
  missing: [],
  ...over,
});

/** A review as the server sends one back: who looked, and when. Never a platform verdict. */
const review = (over: Partial<DealerDocumentReview> = {}): DealerDocumentReview => ({
  reviewedAt: '2026-09-02T09:00:00Z',
  reviewedByUserId: '01a08222-79ac-7561-b80f-c8c1778cd4c7',
  reviewedByName: 'Layla Haddad',
  ...over,
});

/** The server's stable ProblemDetails code for "your window has closed". */
const CLOSED = 'booking.renter_documents_not_available';

const panel = (
  status: string,
  value: RenterDocuments | undefined,
  code?: string,
  t: Translate = en,
) => toRenterDocumentsPanel(status, value, code, BOOKING, href, t, date);

describe('the renter-documents panel', () => {
  describe('what a failure means', () => {
    it('treats the access-closed code as the rule working, not as a broken platform', () => {
      // The distinction the whole panel turns on. This code is the server saying "this booking is no
      // longer live, so your window has closed" — a correct, expected answer. Wording it as a
      // failure would tell a gallery the platform is broken when it is behaving as designed, which
      // is precisely what the frontend rules warn about.
      const result = panel('error', undefined, CLOSED);

      expect(result.kind).toBe('closed');
      expect(result.kind === 'closed' && result.note).toBe(
        'A renter’s documents are shown only while this booking is live.',
      );
    });

    it('treats every other refusal as a failure the gallery can retry', () => {
      for (const code of [
        undefined,
        'booking.not_found',
        'documents.not_found',
        'auth.user_not_found',
        'rate_limited',
      ]) {
        const result = panel('error', undefined, code);
        expect(result.kind, `code ${code}`).toBe('failed');
      }
    });

    it('keys on the code, not on the status class it happens to carry', () => {
      // 409 is a category; "your window has closed" is one answer inside it. A future conflict on
      // these routes must not be worded to a gallery as "your access has expired".
      expect(panel('error', undefined, 'booking.dispute_open').kind).toBe('failed');
    });

    it('does not read a permission verdict out of a failed request', () => {
      // A bodiless 403 — the shape a role failure has — carries no code at all, and must NOT become
      // "your access expired". The panel's answer is the same as for a dropped connection: retry.
      const result = panel('error', undefined, undefined);

      expect(result.kind).toBe('failed');
      expect(result.kind === 'failed' && result.note).toBe(
        'The documents could not be loaded. Nothing has changed.',
      );
    });

    it('waits rather than guessing while the request is in flight', () => {
      expect(panel('loading', undefined).kind).toBe('loading');
    });
  });

  describe('what the gallery is given to open', () => {
    it('addresses a document by the two ids it was given and nothing else', () => {
      // The security property of the whole screen, asserted as a string: no storage key, no
      // signature, no expiry, no customer id. If somebody later threads a key through the DTO, this
      // is what fails.
      const result = panel('resolved', answer());
      const [tile] = result.kind === 'ready' ? result.tiles : [];

      expect(tile.href).toBe(`/api/v1/bookings/${BOOKING}/renter-documents/${DOCUMENT}`);
      expect(tile.href).not.toMatch(/signature|expires|customers\/|storage|supabase/i);
    });

    it('names the format the SERVER will serve, never one guessed from the type', () => {
      // A licence on file as a PDF told an admin they were opening a photograph, once. Same trap
      // here, and the same fix: the content type is the server's answer.
      const result = panel(
        'resolved',
        answer({ documents: [document({ contentType: 'application/pdf' })] }),
      );
      const [tile] = result.kind === 'ready' ? result.tiles : [];

      expect(tile.format).toBe('PDF');
      // And it is not offered as an inline image, because a PDF is not an <img>.
      expect(tile.isImage).toBe(false);
    });

    it('offers an inline preview only for the formats a browser can render in place', () => {
      for (const [contentType, inline] of [
        ['image/jpeg', true],
        ['image/png', true],
        ['image/webp', true],
        ['application/pdf', false],
      ] as const) {
        const result = panel('resolved', answer({ documents: [document({ contentType })] }));
        const [tile] = result.kind === 'ready' ? result.tiles : [];
        expect(tile.isImage, contentType).toBe(inline);
      }
    });

    it('marks both sides of the licence as the check spec 5.1 asks for', () => {
      const result = panel(
        'resolved',
        answer({
          documents: [
            document({ documentId: 'a', type: 'DrivingLicenceFront' }),
            document({ documentId: 'b', type: 'DrivingLicenceBack' }),
            document({ documentId: 'c', type: 'NationalId' }),
            document({ documentId: 'd', type: 'Passport' }),
          ],
        }),
      );
      const tiles = result.kind === 'ready' ? result.tiles : [];

      expect(tiles.filter((tile) => tile.isLicence).map((tile) => tile.documentId)).toEqual([
        'a',
        'b',
      ]);
    });

    it('shows a document nobody at the dealership has checked as not reviewed', () => {
      const result = panel('resolved', answer());
      const [tile] = result.kind === 'ready' ? result.tiles : [];

      expect(tile.isReviewed).toBe(false);
      expect(tile.status).toBe('Not reviewed');
      expect(tile.tone).toBe('warn');
      expect(tile.reviewedBy).toBeNull();
    });

    it('formats the upload date through the caller rather than in the presenter', () => {
      // Dates are FormatService's job: it follows the language and pins Latin digits under ar-JO.
      const result = panel('resolved', answer());
      const [tile] = result.kind === 'ready' ? result.tiles : [];

      expect(tile.uploadedAt).toBe('formatted:2026-09-01T09:00:00Z');
    });
  });

  describe('what is absent', () => {
    it('says nothing when the renter has filed everything', () => {
      const result = panel('resolved', answer({ isComplete: true, missing: [] }));

      expect(result.kind === 'ready' && result.missing).toBeNull();
      expect(result.kind === 'ready' && result.isComplete).toBe(true);
    });

    it('names the missing documents the server listed, in the server’s order', () => {
      // The server decides this: it is the customer's own record that knows whether a passport or a
      // national ID is owed. The panel only turns the list into a sentence.
      const result = panel(
        'resolved',
        answer({ documents: [], missing: ['DrivingLicenceBack', 'Passport'] }),
      );

      expect(result.kind === 'ready' && result.missing).toBe(
        'Not on file: Driving licence — back, Passport',
      );
    });

    it('is an empty panel, not an error, when the renter has uploaded nothing', () => {
      // The booking exists and the gallery may see it. "They have filed nothing" is a true answer
      // about it, and must not look like a failure or like a booking that is not theirs.
      const result = panel(
        'resolved',
        answer({
          documents: [],
          missing: ['DrivingLicenceFront', 'DrivingLicenceBack', 'NationalId'],
        }),
      );

      expect(result.kind).toBe('ready');
      expect(result.kind === 'ready' && result.tiles).toEqual([]);
      expect(result.kind === 'ready' && result.missing).toContain('Driving licence — front');
    });
  });

  describe('Arabic', () => {
    it('reads in Arabic throughout, including the sentence naming what is missing', () => {
      const result = panel(
        'resolved',
        answer({ documents: [document()], missing: ['NationalId'] }),
        undefined,
        ar,
      );
      const [tile] = result.kind === 'ready' ? result.tiles : [];

      expect(tile.label).toBe('رخصة القيادة — الوجه الأمامي');
      expect(tile.format).toBe('صورة JPEG');
      // The Arabic comma, not the Latin one: a list joined with ", " reads as broken Arabic.
      expect(result.kind === 'ready' && result.missing).toBe('غير مرفوع: ⁨الهوية الوطنية⁩');
    });

    it('joins several missing documents with the Arabic comma', () => {
      const result = panel(
        'resolved',
        answer({ documents: [], missing: ['DrivingLicenceFront', 'Passport'] }),
        undefined,
        ar,
      );

      const missing = result.kind === 'ready' ? (result.missing ?? '') : '';
      expect(missing).toContain('،');
      expect(missing).not.toContain(', ');
    });

    it('says the access rule closed in Arabic too', () => {
      const result = panel('error', undefined, CLOSED, ar);

      expect(result.kind === 'closed' && result.note).toBe(
        'تُعرض وثائق المستأجر فقط ما دام هذا الحجز قائماً.',
      );
    });

    it('carries no Latin copy into the Arabic panel', () => {
      // The failure this catches is a key added to en.ts and pasted into ar.ts untranslated, which
      // the dictionaries spec catches globally — here it is checked on the words this screen shows.
      const result = panel('error', undefined, 'booking.not_found', ar);
      const note = result.kind === 'failed' ? result.note : '';

      expect(note).not.toMatch(/[A-Za-z]/);
    });

    it('says "reviewed by the dealership" in Arabic, and never "verified"', () => {
      const result = panel(
        'resolved',
        answer({ documents: [document({ dealerReview: review() })] }),
        undefined,
        ar,
      );
      const [tile] = result.kind === 'ready' ? result.tiles : [];

      expect(tile.status).toBe('تمت مراجعتها من المعرض');
      // The word the owner ruled out. Arabic for "verified" is تحقق / موثّقة — neither belongs on a
      // badge about what a GALLERY did.
      expect(tile.status).not.toContain('موثّق');
      expect(tile.status).not.toContain('تحقق');
    });

    it('names the unreviewed state in Arabic too', () => {
      const result = panel('resolved', answer(), undefined, ar);
      const [tile] = result.kind === 'ready' ? result.tiles : [];

      expect(tile.status).toBe('لم تُراجَع');
      expect(tile.status).not.toMatch(/[A-Za-z]/);
    });
  });

  describe('the dealership’s own review', () => {
    it('shows a reviewed document as reviewed BY THE DEALER, never as verified', () => {
      // The wording the owner ruled on. "Reviewed by dealer" records that a gallery looked; it must
      // never read as a Khadra guarantee of authenticity.
      const result = panel(
        'resolved',
        answer({ documents: [document({ dealerReview: review() })] }),
      );
      const [tile] = result.kind === 'ready' ? result.tiles : [];

      expect(tile.isReviewed).toBe(true);
      expect(tile.status).toBe('Reviewed by dealer');
      expect(tile.status).not.toMatch(/verif/i);
      expect(tile.tone).toBe('ok');
    });

    it('names who recorded it and when', () => {
      const result = panel(
        'resolved',
        answer({ documents: [document({ dealerReview: review() })] }),
      );
      const [tile] = result.kind === 'ready' ? result.tiles : [];

      expect(tile.reviewedBy).toBe('Layla Haddad · formatted:2026-09-02T09:00:00Z');
    });

    it('trusts the server about whether a review still stands', () => {
      // The panel NEVER compares the review date with the upload date. The server clears the review
      // when the renter replaces the file, because it holds the upload instant the review was made
      // against; a screen re-deriving that would drift in the direction that matters — a
      // re-photographed licence still showing as checked.
      const stale = panel(
        'resolved',
        answer({
          documents: [document({ uploadedAt: '2026-09-09T09:00:00Z', dealerReview: null })],
        }),
      );
      const [tile] = stale.kind === 'ready' ? stale.tiles : [];

      expect(tile.isReviewed).toBe(false);
      expect(tile.status).toBe('Not reviewed');
    });

    it('carries no platform verdict anywhere on the tile', () => {
      // CustomerDocument.Status can take the value "Verified" and is deliberately not on the wire.
      // If it ever comes back, this is what fails.
      const result = panel(
        'resolved',
        answer({ documents: [document({ dealerReview: review() })] }),
      );
      const rendered = JSON.stringify(result);

      expect(rendered).not.toMatch(/PendingReview|"Verified"|Rejected/);
    });
  });
});
