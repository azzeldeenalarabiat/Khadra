import { describe, expect, it } from 'vitest';
import { TranslationKey } from './en';
import {
  fieldMessage,
  fieldMessageFor,
  problemMessage,
  serverSentence,
  snapshotProblem,
} from './problem';
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

describe('a validation key is a path, not a flat name', () => {
  /**
   * FluentValidation prepends the parent property of every child validator, so where a field sits in
   * the request decides how deep its key is. The fleet's `Make` arrives as `details.Make` — the car's
   * fields travel inside `Details` on the command — and the console asks for `make`, which is the
   * only name the screen has. It found nothing, so the fleet form's per-field messages never showed.
   */
  const nested = snapshotProblem({
    status: 400,
    error: {
      errors: {
        'details.Make': ['Make is required.'],
        'details.Description.Ar': ['Arabic description is too long.'],
        'about.En': ['English About is too long.'],
        DescriptionAr: ['Arabic description is too long.'],
      },
    },
  });

  it('answers for a field wrapped in a parent it does not know about', () => {
    expect(fieldMessage(nested, 'make', 'en', t)).toBe('Make is required.');
    expect(fieldMessage(nested, 'description.ar', 'en', t)).toBe(
      'Arabic description is too long.',
    );
  });

  it('matches whole segments, never a substring of one', () => {
    // `escription.ar` is not a field, and a substring match would have made it one.
    expect(fieldMessage(nested, 'escription.ar', 'en', t)).toBeNull();
    // A tail is a tail: the wrapper cannot be asked for on its own, and neither can a head of the
    // path. Only what the key ENDS with.
    expect(fieldMessage(nested, 'details', 'en', t)).toBeNull();
    expect(fieldMessage(nested, 'details.Description', 'en', t)).toBeNull();
  });

  it('does not let a section answer for one of its languages', () => {
    // `about` is a real key in its own right — the validator's `NotNull` on the whole section uses
    // it — and it must not be satisfied by `about.En`, or a refusal about one box would be reported
    // as a refusal about the section and shown under both.
    expect(fieldMessage(nested, 'about', 'en', t)).toBeNull();
    expect(fieldMessage(nested, 'about.en', 'en', t)).toBe('English About is too long.');
  });

  it('answers a one-segment question with any key that ends in it', () => {
    // The price of tail matching, stated rather than discovered: `ar` is the last segment of
    // `details.Description.Ar`, so asking for `ar` alone finds it. That is what makes `make` find
    // `details.Make`, and no screen asks for a bare language tag — they ask through `boxErrorNames`,
    // which always sends at least a field and a language.
    expect(fieldMessage(nested, 'ar', 'en', t)).toBe('Arabic description is too long.');
  });

  it('will not answer for a longer path than the key has', () => {
    expect(fieldMessage(nested, 'vehicle.about.En', 'en', t)).toBeNull();
  });

  it('tries several names for one box and takes the first that answers', () => {
    // The dotted name and the flat one, in that order: the same refusal arrives under either
    // depending on which layer produced it. See `boxErrorNames`.
    expect(fieldMessageFor(nested, ['insurance.ar', 'insuranceAr'], 'en', t)).toBeNull();
    expect(fieldMessageFor(nested, ['description.ar', 'descriptionAr'], 'en', t)).toBe(
      'Arabic description is too long.',
    );
    // The flat spelling on its own, which is what the multipart binder on the application form uses.
    const flatOnly = snapshotProblem({
      status: 400,
      error: { errors: { DescriptionAr: ['Too long.'] } },
    });
    expect(fieldMessageFor(flatOnly, ['description.ar', 'descriptionAr'], 'en', t)).toBe(
      'Too long.',
    );
  });

  it('is null when none of the names is mentioned', () => {
    expect(fieldMessageFor(nested, ['pickupInstructions.ar', 'pickupInstructionsAr'], 'en', t))
      .toBeNull();
    expect(fieldMessageFor(null, ['about.en'], 'en', t)).toBeNull();
  });
});
