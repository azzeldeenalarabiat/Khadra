import { describe, expect, it } from 'vitest';
import { formatFrozenLocal } from './date-format';

const EN = 'en-GB';
const AR = 'ar-JO-u-nu-latn';
const OPTIONS: Intl.DateTimeFormatOptions = {
  day: 'numeric',
  month: 'short',
  year: 'numeric',
  hour: '2-digit',
  minute: '2-digit',
  hourCycle: 'h23',
};

/**
 * A wall time an issued document froze in Amman (payments Phase 5b): printed as that same wall time, on
 * any machine. It is placed in UTC and formatted in UTC, so no zone — the browser's included — can
 * move it; re-deriving it from the document's UTC instant would re-judge a record by today's rules.
 */
describe('formatFrozenLocal', () => {
  it('prints the stored wall time, not one moved through a zone', () => {
    const text = formatFrozenLocal('2026-09-27 11:17', EN, OPTIONS);
    expect(text).toContain('27 Sept 2026');
    expect(text).toContain('11:17');
  });

  it('keeps a time near midnight on its own day and year', () => {
    const text = formatFrozenLocal('2026-12-31 23:30', EN, OPTIONS);
    expect(text).toContain('31 Dec 2026');
    expect(text).toContain('23:30');
  });

  it('prints Arabic with Latin digits', () => {
    const text = formatFrozenLocal('2026-09-27 11:17', AR, OPTIONS)!;
    expect(text).toContain('27');
    expect(text).toContain('2026');
    expect(text).toContain('11:17');
    expect(text).not.toMatch(/[٠-٩]/);
  });

  it('answers null for text that is not such a time, for the caller to print as it is', () => {
    expect(formatFrozenLocal('2026-09-27T11:17:00Z', EN, OPTIONS)).toBeNull();
    expect(formatFrozenLocal('2026-9-27 11:17', EN, OPTIONS)).toBeNull();
    expect(formatFrozenLocal('', EN, OPTIONS)).toBeNull();
    expect(formatFrozenLocal(null, EN, OPTIONS)).toBeNull();
  });

  it('never rolls an impossible time over into a different, plausible one', () => {
    expect(formatFrozenLocal('2026-13-45 99:99', EN, OPTIONS)).toBeNull();
    expect(formatFrozenLocal('2026-02-30 10:00', EN, OPTIONS)).toBeNull();
    expect(formatFrozenLocal('2026-09-27 24:00', EN, OPTIONS)).toBeNull();
  });
});
