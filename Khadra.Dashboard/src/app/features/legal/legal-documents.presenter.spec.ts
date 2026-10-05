import { describe, expect, it } from 'vitest';
import { AR } from '../../core/i18n/ar';
import { EN, TranslationKey } from '../../core/i18n/en';
import { MessageParams } from '../../core/i18n/language';
import { ProblemSnapshot, snapshotProblem } from '../../core/i18n/problem';
import { resolveMessage } from '../../core/i18n/resolve';
import {
  legalKindLabel,
  legalRefusal,
  legalStateLabel,
  legalStateTone,
  previewKey,
} from './legal-documents.presenter';

/** The legal texts' screen (Wave 2 G1), worded against the REAL dictionaries in both languages. */
const en = (key: TranslationKey, params?: MessageParams) => resolveMessage(EN[key], params, 'en-GB', false) ?? key;
const ar = (key: TranslationKey, params?: MessageParams) =>
  (resolveMessage(AR[key], params, 'ar-JO-u-nu-latn', true) ?? key).replace(/[⁨⁩]/g, '');

const refused = (body: Record<string, unknown>, status = 400): ProblemSnapshot =>
  snapshotProblem({ status, error: { title: 'Server sentence.', ...body } });

describe('the documents and their versions', () => {
  it('names each document in the reader language, and spells out one this build does not know', () => {
    expect(legalKindLabel('Terms', en)).toBe('Terms of Service');
    expect(legalKindLabel('Privacy', ar)).toBe('إشعار الخصوصية');
    expect(legalKindLabel('CookiePolicy', en)).toBe('Cookie policy');
  });

  it('says which version is in force and which were replaced', () => {
    expect(legalStateLabel('Current', en)).toBe('In force');
    expect(legalStateLabel('Superseded', ar)).toBe('مُستبدَل');
    expect(legalStateTone('Current')).toBe('ok');
    expect(legalStateTone('Superseded')).toBe('dim');
  });
});

describe('a refused preview or publish', () => {
  it('names the language, the line and the reason of a text that cannot be published', () => {
    const problem = refused({ code: 'legal.text_unsupported', language: 'ar', line: 12, reason: 'image' });

    expect(legalRefusal(problem, en, 'en')).toBe('The Arabic text cannot be published as it stands. Line 12 has an image.');
    expect(legalRefusal(problem, ar, 'ar')).toBe('لا يمكن نشر النص العربي كما هو. السطر 12 فيه صورة.');
  });

  it('reads the location only beside its own code, and only when it is whole', () => {
    expect(refused({ code: 'legal.label_taken', language: 'en', line: 3, reason: 'html' }).textProblem).toBeUndefined();
    const partial = refused({ code: 'legal.text_unsupported', language: 'fr', line: 3, reason: 'html' });
    expect(partial.textProblem).toBeUndefined();
    expect(legalRefusal(partial, en, 'en')).toBe('One of the texts uses something that cannot be published.');
  });

  it('words Markdown nested too deeply to render, with the line it starts on', () => {
    const problem = refused({ code: 'legal.text_unsupported', language: 'en', line: 5, reason: 'nesting' });

    expect(legalRefusal(problem, en, 'en')).toBe(
      'The English text cannot be published as it stands. Line 5 nests lists, quotes or emphasis deeper than a page can show.',
    );
    expect(legalRefusal(problem, ar, 'ar')).toContain('يضع القوائم أو الاقتباسات أو التنسيق بعضها داخل بعض');
  });

  it('words a reason this build does not know as unpublished Markdown', () => {
    const problem = refused({ code: 'legal.text_unsupported', language: 'en', line: 2, reason: 'tables' });

    expect(legalRefusal(problem, en, 'en')).toBe(
      'The English text cannot be published as it stands. Line 2 uses Markdown that is not published.',
    );
  });

  it.each([
    'legal.label_invalid',
    'legal.label_taken',
    'legal.body_required',
    'legal.body_too_long',
    'legal.body_invalid_characters',
    'legal.publish_conflict',
    'legal.kind_unknown',
    'legal.version_not_found',
  ])('words %s itself in both languages', (code) => {
    expect(legalRefusal(refused({ code }), en, 'en')).not.toBe('Server sentence.');
    expect(legalRefusal(refused({ code }), ar, 'ar')).toMatch(/[؀-ۿ]/);
  });

  it("falls back to the server's sentence in English and the console's own line in Arabic", () => {
    expect(legalRefusal(refused({ code: 'legal.something_new' }), en, 'en')).toBe('Server sentence.');
    expect(legalRefusal(refused({ code: 'legal.something_new' }), ar, 'ar')).toBe(ar('common.requestRefused'));
  });
});

describe('what a preview was taken of', () => {
  const request = { kind: 'Terms' as const, versionLabel: '2026-10', bodyEn: '# Terms', bodyAr: '# الشروط' };

  it('matches the same form, the label trimmed as the server stores it', () => {
    expect(previewKey({ ...request, versionLabel: ' 2026-10 ' })).toBe(previewKey(request));
  });

  it('no longer matches once anything that would be published changes', () => {
    expect(previewKey({ ...request, bodyAr: '# الشروط.' })).not.toBe(previewKey(request));
    expect(previewKey({ ...request, kind: 'Privacy' })).not.toBe(previewKey(request));
    expect(previewKey({ ...request, versionLabel: '2026-11' })).not.toBe(previewKey(request));
  });
});
