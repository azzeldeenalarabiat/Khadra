import { describe, expect, it } from 'vitest';
import { AppConfig } from '../api/app-config.api';
import { EN, TranslationKey } from '../i18n/en';
import { problemText } from './problem-text';
import { ProblemSnapshot } from './problem';

const t = (key: TranslationKey, params?: Record<string, string | number>) =>
  String(EN[key]).replace(/\{(\w+)\}/g, (_, name: string) => String(params?.[name] ?? `{${name}}`));

const problem = (overrides: Partial<ProblemSnapshot>): ProblemSnapshot => ({
  status: 400,
  code: null,
  title: null,
  traceId: null,
  errors: null,
  ...overrides,
});

describe('problemText', () => {
  it('words a known code in the page language', () => {
    expect(problemText(problem({ code: 'auth.email_taken' }), t, 'ar', null)).toBe(EN['problem.emailTaken']);
  });

  it('takes the minimum age from the published configuration, never a constant', () => {
    const config = { minimumRenterAge: 23 } as AppConfig;
    expect(problemText(problem({ code: 'auth.under_minimum_age' }), t, 'en', config)).toContain('23');
    expect(problemText(problem({ code: 'auth.under_minimum_age' }), t, 'en', null)).toBe(
      EN['problem.underMinimumAgeNoFigure'],
    );
  });

  it('shows the server sentence only on an English page', () => {
    const refusal = problem({ code: 'something.new', title: 'Something new went wrong.' });
    expect(problemText(refusal, t, 'en', null)).toBe('Something new went wrong.');
    expect(problemText(refusal, t, 'ar', null)).toBe(EN['problem.unknown']);
  });

  it('reads an unreachable server and a throttle as such', () => {
    expect(problemText(problem({ status: 0 }), t, 'en', null)).toBe(EN['state.unavailable.title']);
    expect(problemText(problem({ status: 503 }), t, 'en', null)).toBe(EN['state.unavailable.title']);
    expect(problemText(problem({ status: 429, code: 'rate_limited' }), t, 'en', null)).toBe(EN['problem.rateLimited']);
  });
});
