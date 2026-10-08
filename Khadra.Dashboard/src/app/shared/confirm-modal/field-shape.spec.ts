import { describe, expect, it } from 'vitest';
import { ModalField } from '../../core/models/console.models';
import { fieldShapeProblem } from './field-shape';

const email: ModalField = { name: 'email', label: 'Email', type: 'line', inputMode: 'email' };
const phone: ModalField = { name: 'phone', label: 'Phone', type: 'line', inputMode: 'tel' };

/**
 * The dialog's own check of an address or a number (Wave 5, F88). It must never refuse what the server accepts — the
 * server owns the rule and words its refusal in either language — so every case the domain's `EmailAddress` and
 * `PhoneNumber` accept is listed here as passing.
 */
describe('fieldShapeProblem', () => {
  it.each(['ahmad@example.jo', ' Ahmad.Zaid+staff@Example.CO.JO ', 'a@b.cd'])('lets %j through', (value) => {
    expect(fieldShapeProblem(email, value)).toBeNull();
  });

  it.each(['ahmad', 'ahmad@example', 'ahmad zaid@example.jo', '@example.jo', 'a@@b.jo'])(
    'stops %j, which is not an address',
    (value) => {
      expect(fieldShapeProblem(email, value)).toBe('modalField.emailShape');
    },
  );

  it.each(['0791234567', '+962791234567', '00962791234567', '962791234567', '079 123 4567', '(079) 123-4567', '+12025550123', '+4930123456'])(
    'lets the number %j through, as the server reads it',
    (value) => {
      expect(fieldShapeProblem(phone, value)).toBeNull();
    },
  );

  it.each(['0791', 'ahmad@example.jo', 'call me'])('stops %j, which is not a number', (value) => {
    expect(fieldShapeProblem(phone, value)).toBe('modalField.phoneShape');
  });

  it('leaves an empty box to the required check, and every other field alone', () => {
    expect(fieldShapeProblem(email, '   ')).toBeNull();
    expect(fieldShapeProblem({ name: 'reason', label: 'Reason', type: 'text' }, 'no @ here')).toBeNull();
    expect(fieldShapeProblem({ name: 'code', label: 'Code', type: 'line', inputMode: 'numeric' }, '12')).toBeNull();
  });
});

/**
 * The sources, read from disk. The specs run under Node but carry no Node type definitions, so its file API is reached
 * at run time rather than imported.
 */
interface FileSystem {
  readFileSync(path: string, encoding: 'utf8'): string;
  readdirSync(path: string, options: { recursive: true }): string[];
}
const nodeModule = (name: string): Promise<FileSystem> => import(/* @vite-ignore */ name);
const projectRoot = (globalThis as unknown as { process: { cwd(): string } }).process.cwd();

/**
 * F88: the dealer's staff invitation asked for an email and a phone number in two textareas, after the admin's own
 * invitation had been moved off them. Every dialog field named `email` or `phone`, on any screen, is a single line with
 * the keyboard and the input type that go with it.
 */
describe('a dialog that asks for an email or a phone number', () => {
  it('asks on a single line, as an address or a number', async () => {
    const fs = await nodeModule('node:fs');
    const app = `${projectRoot}/src/app`;
    const found: Record<string, string> = {};
    for (const file of fs.readdirSync(app, { recursive: true })) {
      if (!file.endsWith('.component.ts') && !file.endsWith('decisions.ts')) continue;
      const source = fs.readFileSync(`${app}/${file}`, 'utf8');
      for (const match of source.matchAll(/name: '(email|phone)',[^}]*?type: '(\w+)'(?:,[^}]*?inputMode: '(\w+)')?/gs)) {
        found[`${file} ${match[1]}`] = `${match[2]} ${match[3] ?? '-'}`;
      }
    }
    expect(Object.keys(found).length).toBeGreaterThanOrEqual(4);
    for (const [where, shape] of Object.entries(found)) {
      const wanted = where.endsWith(' email') ? 'line email' : 'line tel';
      expect(shape, where).toBe(wanted);
    }
  });
});
