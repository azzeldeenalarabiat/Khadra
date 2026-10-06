import { describe, expect, it } from 'vitest';

import { AR } from '../../core/i18n/ar';
import { EN, TranslationKey } from '../../core/i18n/en';
import { ProblemSnapshot } from '../../core/i18n/problem';
import { resolveMessage } from '../../core/i18n/resolve';
import { describeApplicationRefusal } from './dealer-apply.presenter';

const en = (key: TranslationKey) => resolveMessage(EN[key], undefined, 'en-GB', false) ?? key;
const ar = (key: TranslationKey) => resolveMessage(AR[key], undefined, 'ar-JO-u-nu-latn', true) ?? key;

function refused(code: string, title = 'Refused.'): ProblemSnapshot {
  return { status: 400, code, title, traceId: 't-1', errors: null };
}

describe('describeApplicationRefusal', () => {
  it('words the platform’s own name in both languages (pre-launch item 230)', () => {
    const problem = refused('dealer.business_name_reserved');
    expect(describeApplicationRefusal(problem, en, 'en')).toBe(en('dealerApply.nameReserved'));
    expect(describeApplicationRefusal(problem, ar, 'ar')).toBe(ar('dealerApply.nameReserved'));
  });

  it('words a missing document from the code the server actually sends (F68)', () => {
    const problem = refused('dealer.missing_documents');
    expect(describeApplicationRefusal(problem, en, 'en')).toBe(en('dealerApply.allThreeDocumentsAre'));
    expect(describeApplicationRefusal(problem, ar, 'ar')).toBe(ar('dealerApply.allThreeDocumentsAre'));
  });

  it('falls back to the server’s English, and to the console’s own line in Arabic', () => {
    const problem = refused('dealer.something_new', 'Something the console does not know.');
    expect(describeApplicationRefusal(problem, en, 'en')).toBe('Something the console does not know.');
    expect(describeApplicationRefusal(problem, ar, 'ar')).not.toContain('Something');
  });
});
