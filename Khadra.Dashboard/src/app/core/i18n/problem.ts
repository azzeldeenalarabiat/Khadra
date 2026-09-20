import { TranslationKey } from './en';
import { Language } from './language';

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
