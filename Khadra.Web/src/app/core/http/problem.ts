/**
 * What a failed request said, kept as facts rather than as a sentence, so a language switch re-words
 * it like everything else on the page. Same shape as the console's `ProblemSnapshot`.
 */
export interface ProblemSnapshot {
  /** The HTTP status, or 0 when the request never got an answer. */
  readonly status: number;
  /** The server's stable `code` — the only part of a refusal a client may translate. */
  readonly code: string | null;
  /** The ProblemDetails `title`, which the API writes in English. */
  readonly title: string | null;
  readonly traceId: string | null;
  readonly errors: Readonly<Record<string, readonly string[]>> | null;
}

export function snapshotProblem(error: unknown): ProblemSnapshot {
  const failure = (typeof error === 'object' && error !== null ? error : {}) as { status?: unknown; error?: unknown };
  const body = (typeof failure.error === 'object' && failure.error !== null ? failure.error : {}) as {
    code?: unknown;
    title?: unknown;
    traceId?: unknown;
    errors?: unknown;
  };
  return {
    status: typeof failure.status === 'number' ? failure.status : 0,
    code: typeof body.code === 'string' ? body.code : null,
    title: typeof body.title === 'string' && body.title.trim() !== '' ? body.title : null,
    traceId: typeof body.traceId === 'string' ? body.traceId : null,
    errors: isFieldErrors(body.errors) ? body.errors : null,
  };
}

function isFieldErrors(value: unknown): value is Record<string, string[]> {
  return (
    typeof value === 'object' &&
    value !== null &&
    Object.values(value).every((entry) => Array.isArray(entry) && entry.every((item) => typeof item === 'string'))
  );
}
