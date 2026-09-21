import { describe, expect, it } from 'vitest';
import { TranslationKey } from './en';
import { fieldMessage, problemMessage, serverSentence, snapshotProblem } from './problem';
import { MessageParams } from './language';

/** Shows which key was chosen, so an assertion cannot pass on the English sentence by accident. */
const t = (key: TranslationKey, params?: MessageParams): string =>
  params ? `«${key}:${Object.values(params).join(',')}»` : `«${key}»`;

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

/**
 * The refusals the Admin console used to lose.
 *
 * Every one of these answered "That was refused. Nothing has been changed." on an Arabic screen —
 * the whole of what an administrator was told when an invitation would not go through. The point of
 * each case below is that two different refusals must not read the same.
 */
describe('problemMessage', () => {
  const refusal = (status: number, error: Record<string, unknown>) =>
    snapshotProblem({ status, error });

  it('words a taken email address in either language', () => {
    const taken = refusal(409, {
      code: 'auth.email_taken',
      title: 'An account with this email already exists.',
    });

    expect(problemMessage(taken, 'ar', t)).toBe('«problem.emailTaken»');
    expect(problemMessage(taken, 'en', t)).toBe('«problem.emailTaken»');
  });

  it('tells a taken phone number apart from a taken address', () => {
    const email = refusal(409, { code: 'auth.email_taken', title: 'x' });
    const phone = refusal(409, { code: 'auth.phone_taken', title: 'x' });

    expect(problemMessage(phone, 'ar', t)).not.toBe(problemMessage(email, 'ar', t));
  });

  it('words a malformed address from the code the API now sends', () => {
    // Before the controller stopped carrying [EmailAddress] this arrived as a bare model-validation
    // failure with no code at all — the second case below, which is why both are covered.
    const coded = refusal(400, { code: 'auth.invalid_email', title: 'The email address is not valid.' });
    expect(problemMessage(coded, 'ar', t)).toBe('«problem.invalidEmail»');
  });

  it('falls back to the one field a validation failure named', () => {
    const named = refusal(400, {
      title: 'One or more validation errors occurred.',
      errors: { Email: ['The Email field is not a valid e-mail address.'] },
    });

    expect(problemMessage(named, 'ar', t)).toBe('«problem.invalidEmail»');
  });

  it("shows the server's own sentence to an English reader when it has no code", () => {
    const unmapped = refusal(409, { code: 'booking.not_live', title: 'Booking is not live.' });
    expect(problemMessage(unmapped, 'en', t)).toBe('Booking is not live.');
  });

  it('says what the status meant, with the trace id, when it can say no more', () => {
    const unmapped = refusal(409, {
      code: 'booking.not_live',
      title: 'Booking is not live.',
      traceId: '00-abc',
    });

    // Arabic gets the console's own words rather than the English sentence — but words that
    // distinguish a conflict from a lapsed session, which is what was missing.
    expect(problemMessage(unmapped, 'ar', t)).toBe('«problem.conflict» · «problem.reference:00-abc»');
  });

  it('distinguishes a lapsed session, a forbidden action and a fallen-over service', () => {
    const sentences = [401, 403, 500].map((status) =>
      problemMessage(refusal(status, { title: 'x' }), 'ar', t),
    );

    expect(new Set(sentences).size).toBe(3);
  });

  it('has nothing to say when the request never got an answer', () => {
    expect(problemMessage(snapshotProblem(new Error('offline')), 'ar', t)).toBeNull();
    expect(problemMessage(snapshotProblem(new Error('offline')), 'en', t)).toBeNull();
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
