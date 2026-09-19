import { describe, expect, it } from 'vitest';
import { TranslationKey } from './en';
import { fieldMessage, serverSentence, snapshotProblem } from './problem';

/** Shows which key was chosen, so an assertion cannot pass on the English sentence by accident. */
const t = (key: TranslationKey): string => `«${key}»`;

describe('snapshotProblem', () => {
  it('keeps the facts of a refusal rather than a sentence', () => {
    const problem = snapshotProblem({
      status: 409,
      error: {
        code: 'booking.not_live',
        title: 'Booking is not live.',
        traceId: '00-abc',
        errors: { Reason: ['Reason is required.'] },
      },
    });

    expect(problem).toEqual({
      status: 409,
      code: 'booking.not_live',
      title: 'Booking is not live.',
      traceId: '00-abc',
      errors: { Reason: ['Reason is required.'] },
    });
  });

  it('reads a request that never got an answer as status 0 with nothing to say', () => {
    expect(snapshotProblem(new Error('offline'))).toEqual({
      status: 0,
      code: null,
      title: null,
      traceId: null,
      errors: null,
    });
    expect(snapshotProblem(null).status).toBe(0);
  });
});

describe('serverSentence', () => {
  const refused = snapshotProblem({ status: 409, error: { title: 'Booking is not live.' } });

  it("shows the server's own sentence to an English reader", () => {
    expect(serverSentence(refused, 'en', t)).toBe('Booking is not live.');
  });

  it('never puts that English sentence on an Arabic screen', () => {
    expect(serverSentence(refused, 'ar', t)).toBe('«common.requestRefused»');
  });

  it('has nothing to say when the server said nothing', () => {
    expect(serverSentence(snapshotProblem({ status: 0 }), 'ar', t)).toBeNull();
  });
});

describe('fieldMessage', () => {
  const invalid = snapshotProblem({
    status: 400,
    error: { errors: { BusinessName: ['Business name is required.'] } },
  });

  it('finds a field whatever case the server named it in', () => {
    expect(fieldMessage(invalid, 'businessName', 'en', t)).toBe('Business name is required.');
  });

  it('words the field in Arabic without the English message', () => {
    expect(fieldMessage(invalid, 'businessName', 'ar', t)).toBe('«common.fieldRejected»');
  });

  it('is null for a field the server did not mention', () => {
    expect(fieldMessage(invalid, 'description', 'en', t)).toBeNull();
    expect(fieldMessage(null, 'description', 'en', t)).toBeNull();
  });
});
