import { describe, expect, it } from 'vitest';
import { AuditSubjectFacts, auditSubject, customerReference, subjectValue } from './audit-subject';

/**
 * Which facts word an audit entry's subject.
 *
 * The two kinds whose stored label is an English sentence — a dispute, a customer — are worded from
 * facts the entry carries beside it, and nothing is read back out of the sentence. Everything else is
 * shown exactly as it was recorded.
 */
const entry = (over: Partial<AuditSubjectFacts>): AuditSubjectFacts => ({
  entityType: 'Dealer',
  entityId: '0198abcd-1234-7def-8abc-0123456789ab',
  bookingReference: null,
  subjectLabel: 'Aqaba Coast Cars',
  ...over,
});

describe('auditSubject', () => {
  it('names a dispute by the booking it is about, whichever way its label was written', () => {
    // Before 2026-09-27 the label was an English sentence; since then, the bare reference.
    for (const subjectLabel of ['Dispute on KH-NY8AHLNK', 'KH-NY8AHLNK']) {
      const subject = auditSubject(
        entry({ entityType: 'Dispute', bookingReference: 'KH-NY8AHLNK', subjectLabel }),
      );
      expect(subject).toEqual({ kind: 'dispute', reference: 'KH-NY8AHLNK' });
    }
  });

  it('names a booking by its reference', () => {
    const subject = auditSubject(
      entry({ entityType: 'Booking', bookingReference: 'KH-XE5NTW3U', subjectLabel: 'KH-XE5NTW3U' }),
    );
    expect(subject).toEqual({ kind: 'booking', reference: 'KH-XE5NTW3U' });
  });

  it('names a customer by the same eight characters the stored label carries, from the id', () => {
    const id = '0198abcd-1234-7def-8abc-0123456789ab';
    const subject = auditSubject(
      entry({ entityType: 'Customer', entityId: id, subjectLabel: 'Customer 0198abcd' }),
    );

    expect(subject).toEqual({ kind: 'customer', reference: '0198abcd' });
    // The server wrote `Customer {id:N}` cut to 17 characters: "Customer " and these eight.
    expect(`Customer ${customerReference(id)}`).toBe('Customer 0198abcd');
  });

  it('shows every other kind as it was recorded', () => {
    for (const entityType of ['Dealer', 'AdminUser', 'City', 'CarType', 'SomethingNewer']) {
      expect(auditSubject(entry({ entityType }))).toEqual({ kind: 'label', label: 'Aqaba Coast Cars' });
    }
  });

  it('falls back to the stored label rather than guessing, when a fact is missing', () => {
    const dispute = auditSubject(
      entry({ entityType: 'Dispute', bookingReference: null, subjectLabel: 'Dispute on KH-NY8AHLNK' }),
    );
    const customer = auditSubject(
      entry({ entityType: 'Customer', entityId: null, subjectLabel: 'Customer 0198abcd' }),
    );

    expect(dispute).toEqual({ kind: 'label', label: 'Dispute on KH-NY8AHLNK' });
    expect(customer).toEqual({ kind: 'label', label: 'Customer 0198abcd' });
  });

  it('gives a sentence the reference, or the label as recorded', () => {
    expect(subjectValue({ kind: 'dispute', reference: 'KH-NY8AHLNK' })).toBe('KH-NY8AHLNK');
    expect(subjectValue({ kind: 'label', label: 'Aqaba Coast Cars' })).toBe('Aqaba Coast Cars');
  });
});
