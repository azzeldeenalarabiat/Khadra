import { describe, expect, it } from 'vitest';

import { EvidenceDraft, evidenceRefusalText } from './evidence-draft';

const limits = { maximumSizeBytes: 8 * 1024 * 1024, allowedContentTypes: ['image/jpeg', 'application/pdf'] };
const file = (name: string, type: string, size = 10) => new File([new Uint8Array(size)], name, { type });

describe('EvidenceDraft', () => {
  it('keeps every file that the published limits allow, in the order chosen', () => {
    const draft = new EvidenceDraft();
    draft.choose([file('a.jpg', 'image/jpeg'), file('b.pdf', 'application/pdf')], limits);
    draft.choose([file('c.jpg', 'image/jpeg')], limits);

    expect(draft.files().map((f) => f.name)).toEqual(['a.jpg', 'b.pdf', 'c.jpg']);
    expect(draft.refused()).toBeNull();
  });

  it('adds nothing from a choice with a refused file, and names it', () => {
    const draft = new EvidenceDraft();
    draft.choose([file('a.jpg', 'image/jpeg'), file('notes.txt', 'text/plain')], limits);

    expect(draft.files()).toEqual([]);
    expect(draft.refused()).toEqual({ kind: 'wrongType', name: 'notes.txt' });
  });

  it('refuses a file larger than the published size', () => {
    const draft = new EvidenceDraft();
    draft.choose([file('big.pdf', 'application/pdf', limits.maximumSizeBytes + 1)], limits);

    expect(draft.refused()).toEqual({ kind: 'tooLarge', name: 'big.pdf' });
  });

  it('removes one file and clears them all', () => {
    const draft = new EvidenceDraft();
    draft.choose([file('a.jpg', 'image/jpeg'), file('b.pdf', 'application/pdf')], limits);
    draft.remove(0);
    expect(draft.files().map((f) => f.name)).toEqual(['b.pdf']);
    draft.clear();
    expect(draft.files()).toEqual([]);
  });

  it('checks nothing before the limits are known; the server still judges every file', () => {
    const draft = new EvidenceDraft();
    draft.choose([file('notes.txt', 'text/plain')], null);
    expect(draft.files().length).toBe(1);
  });
});

describe('evidenceRefusalText', () => {
  const t = (key: string, params?: Readonly<Record<string, string | number>>) => `${key}:${JSON.stringify(params ?? {})}`;

  it('names the published types for a wrong kind, and the published size for a large file', () => {
    expect(evidenceRefusalText({ kind: 'wrongType', name: 'x' }, limits, t as never, () => '8 MB')).toBe(
      'documents.wrongType:{"types":"JPEG, PDF"}',
    );
    expect(evidenceRefusalText({ kind: 'tooLarge', name: 'x' }, limits, t as never, () => '8 MB')).toBe(
      'documents.tooLarge:{"size":"8 MB"}',
    );
    expect(evidenceRefusalText(null, limits, t as never, () => '8 MB')).toBeNull();
  });
});
