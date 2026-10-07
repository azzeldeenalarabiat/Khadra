import { HttpErrorResponse } from '@angular/common/http';
import { describe, expect, it } from 'vitest';
import { CustomerDocument, CustomerDocuments } from '../../core/api/documents.api';
import { owesARejectedDocument, rejectedDocumentTypes } from './documents-refusal';

/** A refusal over documents, and the customer's own record, read for a file Khadra rejected (Wave 4, W4-9). */
const document = (over: Partial<CustomerDocument> = {}): CustomerDocument => ({
  documentId: 'd-1',
  type: 'DrivingLicenceFront',
  status: 'PendingReview',
  contentType: 'image/jpeg',
  sizeBytes: 1024,
  uploadedAt: '2026-10-07T03:25:13.123456+00:00',
  reviewNote: null,
  ...over,
});

const record = (over: Partial<CustomerDocuments> = {}): CustomerDocuments => ({
  documents: [document()],
  isComplete: true,
  missing: [],
  ...over,
});

describe('a refusal over documents', () => {
  it('names the types Khadra rejected, as the server listed them', () => {
    const refusal = new HttpErrorResponse({
      status: 403,
      error: {
        code: 'booking.documents_incomplete',
        missingDocumentTypes: ['DrivingLicenceFront', 'NationalId'],
        rejectedDocumentTypes: ['DrivingLicenceFront'],
      },
    });

    expect(rejectedDocumentTypes(refusal)).toEqual(['DrivingLicenceFront']);
  });

  it('names none from an older API, or from anything that is not a refusal', () => {
    expect(rejectedDocumentTypes(new HttpErrorResponse({ status: 403, error: { code: 'booking.documents_incomplete' } }))).toEqual([]);
    expect(rejectedDocumentTypes(new HttpErrorResponse({ status: 0 }))).toEqual([]);
    expect(rejectedDocumentTypes(new Error('offline'))).toEqual([]);
    expect(rejectedDocumentTypes(null)).toEqual([]);
  });
});

describe('what the customer still owes', () => {
  it('includes a file Khadra rejected whose type is still owed', () => {
    const owed = record({
      documents: [document({ status: 'Rejected', reviewNote: 'Too blurred.' })],
      isComplete: false,
      missing: ['DrivingLicenceFront', 'NationalId'],
    });

    expect(owesARejectedDocument(owed)).toBe(true);
  });

  it('does not count a rejection that a passport already made good, or a record that is complete', () => {
    const replacedBySlot = record({
      documents: [document({ type: 'NationalId', status: 'Rejected' }), document({ type: 'Passport' })],
      isComplete: false,
      missing: ['DrivingLicenceBack'],
    });

    expect(owesARejectedDocument(replacedBySlot)).toBe(false);
    expect(owesARejectedDocument(record())).toBe(false);
    expect(owesARejectedDocument(undefined)).toBe(false);
  });

  it('is only "upload" when nothing was rejected', () => {
    expect(owesARejectedDocument(record({ documents: [], isComplete: false, missing: ['DrivingLicenceFront'] }))).toBe(false);
  });
});
