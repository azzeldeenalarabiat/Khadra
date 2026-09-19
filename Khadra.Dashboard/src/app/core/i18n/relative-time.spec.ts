import { describe, expect, it } from 'vitest';
import { AR } from './ar';
import { EN, TranslationKey } from './en';
import { MessageParams } from './language';
import { resolveMessage } from './resolve';
import { deadlineReading, durationPhrase, relativeTime, slaReading } from './relative-time';

/** Resolves real English, so the assertions read as the words on the screen. */
const t = (key: TranslationKey, params?: MessageParams): string =>
  resolveMessage(EN[key], params, 'en-GB', false) ?? key;

/** Real Arabic, right to left, with the bidi isolates taken out so the words can be compared. */
const ar = (key: TranslationKey, params?: MessageParams): string =>
  (resolveMessage(AR[key], params, 'ar-JO-u-nu-latn', true) ?? key).replace(/[⁦-⁩]/g, '');

const NOW = Date.parse('2026-09-05T12:00:00Z');
const MINUTE = 60_000;
const HOUR = 60 * MINUTE;

describe('relativeTime', () => {
  it('reads a past moment backwards', () => {
    expect(relativeTime('2026-09-05T09:00:00Z', NOW, 'en-GB')).toBe('3 hr ago');
  });

  /** The bug this replaced: an upcoming pickup was labelled "Just now" in the notifications panel. */
  it('reads an upcoming moment forwards', () => {
    expect(relativeTime('2026-09-05T15:00:00Z', NOW, 'en-GB')).toBe('in 3 hr');
    expect(relativeTime(NOW + 90 * MINUTE, NOW, 'en-GB')).not.toMatch(/now/i);
  });

  it('says now inside half a minute either way', () => {
    expect(relativeTime(NOW + 20_000, NOW, 'en-GB')).toBe('now');
    expect(relativeTime(NOW - 20_000, NOW, 'en-GB')).toBe('now');
  });

  it("uses the locale's own words for the neighbouring days", () => {
    expect(relativeTime(NOW - 24 * HOUR, NOW, 'en-GB')).toBe('yesterday');
    expect(relativeTime(NOW + 24 * HOUR, NOW, 'en-GB')).toBe('tomorrow');
  });

  it('speaks Arabic with Latin digits', () => {
    expect(relativeTime(NOW - 3 * HOUR, NOW, 'ar-JO-u-nu-latn')).toBe('قبل 3 ساعات');
    expect(relativeTime(NOW - 24 * HOUR, NOW, 'ar-JO-u-nu-latn')).toBe('أمس');
  });

  it('never prints a date it could not read', () => {
    expect(relativeTime('not a date', NOW, 'en-GB')).toBe('—');
  });
});

/**
 * A chance that runs out (owner, 2026-09-17): "2h remaining" while it runs, "Expired" once it has
 * gone — never "Nh over", which read as a late payment rather than a request that is finished.
 */
describe('deadlineReading', () => {
  it('counts down in whole units, floored, in the unit a person reads', () => {
    expect(deadlineReading(NOW + 40 * MINUTE, NOW, t)).toEqual({
      text: '40m remaining',
      passed: false,
    });
    expect(deadlineReading(NOW + 2 * HOUR, NOW, t)).toEqual({
      text: '2h remaining',
      passed: false,
    });
    // 1h59m is one hour remaining, never two: a clock that rounds up promises time that is not there.
    expect(deadlineReading(NOW + 119 * MINUTE, NOW, t).text).toBe('1h remaining');
    expect(deadlineReading(NOW + 47 * HOUR + 59 * MINUTE, NOW, t).text).toBe('47h remaining');
    expect(deadlineReading(NOW + 71 * HOUR, NOW, t).text).toBe('2d remaining');
  });

  it('never counts down below one minute', () => {
    expect(deadlineReading(NOW + 5_000, NOW, t).text).toBe('1m remaining');
  });

  it('is expired, with no amount, the moment the deadline passes', () => {
    expect(deadlineReading(NOW, NOW, t)).toEqual({ text: 'Expired', passed: true });
    expect(deadlineReading(NOW - 13 * HOUR, NOW, t)).toEqual({ text: 'Expired', passed: true });
  });

  /** A phone whose clock is behind must not show a dead request as live. */
  it("takes the server's word that it is closed over a clock that still sees time", () => {
    expect(deadlineReading(NOW + 5 * HOUR, NOW, t, true)).toEqual({
      text: 'Expired',
      passed: true,
    });
  });

  it('reads a passed deadline as expired even when the server has not yet said so', () => {
    expect(deadlineReading(NOW - MINUTE, NOW, t, false).passed).toBe(true);
  });

  it('prints nothing for a date it could not read', () => {
    expect(deadlineReading('not a date', NOW, t)).toEqual({ text: '', passed: false });
  });

  it('speaks Arabic in every plural form, through the dictionary', () => {
    expect(deadlineReading(NOW + 1 * HOUR, NOW, ar).text).toBe('متبقي ساعة واحدة');
    expect(deadlineReading(NOW + 2 * HOUR, NOW, ar).text).toBe('متبقي ساعتان');
    expect(deadlineReading(NOW + 5 * HOUR, NOW, ar).text).toBe('متبقي 5 ساعات');
    expect(deadlineReading(NOW + 13 * HOUR, NOW, ar).text).toBe('متبقي 13 ساعة');
    expect(deadlineReading(NOW + 1 * MINUTE, NOW, ar).text).toBe('متبقي دقيقة واحدة');
    expect(deadlineReading(NOW + 2 * MINUTE, NOW, ar).text).toBe('متبقي دقيقتان');
    expect(deadlineReading(NOW + 3 * 24 * HOUR, NOW, ar).text).toBe('متبقي 3 أيام');
    expect(deadlineReading(NOW - HOUR, NOW, ar).text).toBe('انتهت المهلة');
  });
});

/**
 * A promise that can be broken (owner, 2026-09-17): "2h remaining" while it holds, "Overdue by 13h"
 * once broken — by how much it was broken is what an administrator acts on.
 */
describe('slaReading', () => {
  it('counts down like any other clock while the promise holds', () => {
    expect(slaReading(NOW + 7 * HOUR, NOW, t)).toEqual({ text: '7h remaining', passed: false });
    expect(slaReading(NOW + 60 * HOUR, NOW, t).text).toBe('2d remaining');
  });

  it('says by how much a broken promise is overdue, floored', () => {
    expect(slaReading(NOW - 13 * HOUR, NOW, t)).toEqual({ text: 'Overdue by 13h', passed: true });
    expect(slaReading(NOW - 13 * HOUR - 59 * MINUTE, NOW, t).text).toBe('Overdue by 13h');
    expect(slaReading(NOW - 40 * MINUTE, NOW, t).text).toBe('Overdue by 40m');
    expect(slaReading(NOW - 50 * HOUR, NOW, t).text).toBe('Overdue by 2d');
  });

  it('is overdue by at least a minute the moment the deadline passes', () => {
    expect(slaReading(NOW, NOW, t)).toEqual({ text: 'Overdue by 1m', passed: true });
  });

  /** The server says the promise is broken; this clock cannot say by how much without inventing it. */
  it("takes the server's word that it is overdue without inventing an amount", () => {
    expect(slaReading(NOW + 3 * HOUR, NOW, t, true)).toEqual({ text: 'Overdue', passed: true });
  });

  it('keeps an overdue ticket and an expired request apart', () => {
    expect(slaReading(NOW - HOUR, NOW, t).text).not.toBe(deadlineReading(NOW - HOUR, NOW, t).text);
  });

  it('speaks Arabic in every plural form, with the case the sentence needs', () => {
    expect(slaReading(NOW - 1 * HOUR, NOW, ar).text).toBe('متأخر ساعة واحدة');
    // Dual after متأخر is ساعتين, not the ساعتان of "متبقي ساعتان": one whole message per state.
    expect(slaReading(NOW - 2 * HOUR, NOW, ar).text).toBe('متأخر ساعتين');
    expect(slaReading(NOW - 5 * HOUR, NOW, ar).text).toBe('متأخر 5 ساعات');
    expect(slaReading(NOW - 13 * HOUR, NOW, ar).text).toBe('متأخر 13 ساعة');
    expect(slaReading(NOW - 2 * 24 * HOUR, NOW, ar).text).toBe('متأخر يومين');
    expect(slaReading(NOW - 11 * 24 * HOUR, NOW, ar).text).toBe('متأخر 11 يومًا');
    expect(slaReading(NOW + 3 * HOUR, NOW, ar, true).text).toBe('متأخر');
  });
});

describe('durationPhrase', () => {
  it('changes unit where the badges always have', () => {
    expect(durationPhrase(59 * MINUTE, t)).toBe('59m');
    expect(durationPhrase(47 * HOUR, t)).toBe('47h');
    expect(durationPhrase(48 * HOUR, t)).toBe('2d');
  });
});
