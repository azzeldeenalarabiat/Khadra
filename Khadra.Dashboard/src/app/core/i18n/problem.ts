import { TranslationKey } from './en';
import { Language, MessageParams } from './language';

/**
 * What a failed request said, kept as facts rather than as a sentence.
 *
 * Screens used to store the resolved message — `problem.set(error.title ?? t('…'))` — and a stored
 * sentence stays in the language it was written in: switch to Arabic with a refusal on screen and it
 * went on reading English. A screen now holds this snapshot in its signal and chooses the words in a
 * `computed`, so a language switch re-words the refusal like everything else on the page.
 */
export interface ProblemSnapshot {
  /** The HTTP status, or 0 when the request never got an answer. */
  readonly status: number;
  /** The server's stable `code` — the only part of a refusal a client can translate. */
  readonly code: string | null;
  /** The ProblemDetails `title`, which the API writes in English. */
  readonly title: string | null;
  /** Quoted to support; shown beside a refusal the console could not word itself. */
  readonly traceId: string | null;
  /** Per-field messages from a validation failure, as the server wrote them. */
  readonly errors: Readonly<Record<string, readonly string[]>> | null;
}

/** Reads the parts of an `HttpErrorResponse` (or anything shaped like one) that a screen may use. */
export function snapshotProblem(error: unknown): ProblemSnapshot {
  const failure = (typeof error === 'object' && error !== null ? error : {}) as {
    status?: unknown;
    error?: unknown;
  };
  const body = (
    typeof failure.error === 'object' && failure.error !== null ? failure.error : {}
  ) as { code?: unknown; title?: unknown; traceId?: unknown; errors?: unknown };

  return {
    status: typeof failure.status === 'number' ? failure.status : 0,
    code: typeof body.code === 'string' ? body.code : null,
    title: typeof body.title === 'string' && body.title.trim() !== '' ? body.title : null,
    traceId: typeof body.traceId === 'string' ? body.traceId : null,
    errors: isFieldErrors(body.errors) ? body.errors : null,
  };
}

/**
 * The server's own sentence, but only in the language the server writes.
 *
 * The API words ProblemDetails in English. In English that sentence is the most specific thing the
 * console can say about a code it has not mapped, so it is shown. In Arabic it would be English in the
 * middle of an Arabic screen, so the reader gets the console's own "refused" line instead — true about
 * what happened, and in their language. `null` when the server sent no sentence at all, which is the
 * case each screen's "the service did not respond" message is for.
 */
export function serverSentence(
  problem: ProblemSnapshot,
  language: Language,
  t: (key: TranslationKey) => string,
): string | null {
  if (!problem.title) return null;
  return language === 'en' ? problem.title : t('common.requestRefused');
}

/**
 * Refusals this console can word ITSELF, in either language, from the server's stable `code`.
 *
 * The `code` is the only part of a ProblemDetails a client may translate — the `title` is prose the
 * API writes in English — so every entry here is a refusal an operator can act on: a taken address,
 * a malformed number, a rule about the set of administrators. Anything not listed still falls back
 * to the server's own sentence in English and to a status-shaped line in Arabic, which is what this
 * map exists to stop being the ONLY answer.
 */
const WORDED_CODES: Readonly<Record<string, TranslationKey>> = {
  'auth.email_taken': 'problem.emailTaken',
  'auth.phone_taken': 'problem.phoneTaken',
  'auth.invalid_email': 'problem.invalidEmail',
  'auth.invalid_phone': 'problem.invalidPhone',
  'auth.invalid_name': 'problem.invalidName',
  'auth.user_not_found': 'problem.notFound',
  'auth.account_suspended': 'problem.accountSuspended',
  'auth.invalid_token': 'problem.invalidToken',
  'admin.cannot_deactivate_self': 'adminUsers.youCannotDeactivateYour',
  'admin.last_administrator': 'adminUsers.thisIsTheLast',
  'admin.invitation_accepted': 'problem.invitationAccepted',
  'admin.invitation_target_inactive': 'problem.invitationTargetInactive',
  'admin.invitation_email_not_sent': 'problem.invitationEmailNotSent',
};

/** The same, for the fields a validation failure can name. Keys are lower-cased server names. */
const WORDED_FIELDS: Readonly<Record<string, TranslationKey>> = {
  email: 'problem.invalidEmail',
  phone: 'problem.invalidPhone',
  fullname: 'problem.invalidName',
};

/** What a bare status means, when nothing more specific is available. */
function statusSentence(status: number): TranslationKey | null {
  if (status === 401) return 'problem.signedOut';
  if (status === 403) return 'problem.notPermitted';
  if (status === 404) return 'problem.notFound';
  if (status === 409) return 'problem.conflict';
  if (status === 429) return 'problem.tooMany';
  if (status >= 500) return 'problem.unavailable';
  if (status === 400 || status === 422) return 'problem.rejectedDetails';
  return null;
}

/**
 * The most specific sentence this console can say about a refusal, in the language on screen.
 *
 * Resolution runs from the most specific to the least, and stops at the first answer:
 *
 * 1. the server's stable `code`, worded here in both languages;
 * 2. a single named field from a validation failure, worded the same way;
 * 3. the server's own `title` — but only in English, because that is the language it writes;
 * 4. what the HTTP status alone means, with the trace id appended so support has something to
 *    quote.
 *
 * Step 4 is why this exists. Before it, an Arabic operator inviting an administrator whose address
 * was already taken read "That was refused. Nothing has been changed." — true, useless, and
 * identical to a malformed phone number, a lapsed session and a server that fell over. The console
 * knew the difference and threw it away.
 *
 * Returns `null` only when there was no answer at all, which is the case each screen's "the service
 * did not respond" line is for.
 */
export function problemMessage(
  problem: ProblemSnapshot,
  language: Language,
  t: (key: TranslationKey, params?: MessageParams) => string,
): string | null {
  const byCode = problem.code ? WORDED_CODES[problem.code] : undefined;
  if (byCode) return t(byCode);

  const named = problem.errors ? Object.keys(problem.errors) : [];
  if (named.length === 1) {
    const byField = WORDED_FIELDS[named[0].toLowerCase()];
    if (byField) return t(byField);
  }

  if (language === 'en' && problem.title) return problem.title;

  // Null ONLY when the request never got an answer, which is the case each
  // screen's "the service did not respond" line is for. A status this map does
  // not know still happened, and saying nothing responded would be a different
  // and wrong claim — a 200 whose body would not parse lands here.
  if (problem.status === 0) return null;

  const byStatus = statusSentence(problem.status) ?? 'common.requestRefused';

  // The trace id is the one thing that turns "it was refused" into something support can look up,
  // and this is the branch where the console admits it could not say more than that.
  const sentence = t(byStatus);
  return problem.traceId ? `${sentence} · ${t('problem.reference', { traceId: problem.traceId })}` : sentence;
}

/** The server's first message for one field, under the same language rule. Null when there is none. */
export function fieldMessage(
  problem: ProblemSnapshot | null,
  field: string,
  language: Language,
  t: (key: TranslationKey) => string,
): string | null {
  if (!problem?.errors) return null;
  const wanted = field.toLowerCase();
  const name = Object.keys(problem.errors).find((candidate) => candidate.toLowerCase() === wanted);
  const message = name === undefined ? undefined : problem.errors[name]?.[0];
  if (!message) return null;
  return language === 'en' ? message : t('common.fieldRejected');
}

function isFieldErrors(value: unknown): value is Readonly<Record<string, readonly string[]>> {
  return (
    typeof value === 'object' &&
    value !== null &&
    Object.values(value).every(
      (messages) => Array.isArray(messages) && messages.every((m) => typeof m === 'string'),
    )
  );
}
