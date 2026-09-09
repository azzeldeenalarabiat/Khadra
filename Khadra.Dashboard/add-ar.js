/*
 * Appends a batch of Arabic entries to ar.ts, in the file's own formatting.
 * Development tooling: `node add-ar.js batch.json "# comment"`.
 */
const fs = require('fs');

const [, , batchFile, comment] = process.argv;
const batch = JSON.parse(fs.readFileSync(batchFile, 'utf8'));
const target = 'src/app/core/i18n/ar.ts';
let src = fs.readFileSync(target, 'utf8');
const nl = src.includes('\r\n') ? '\r\n' : '\n';

const escape = (s) => (s.includes("'") ? `"${s.replace(/"/g, '\\"')}"` : `'${s}'`);

/*
 * A key already in the file is SKIPPED, not appended again.
 *
 * TypeScript resolves a duplicate property last-wins inside a plain object, but this one is
 * `as const satisfies`, where it is an error -- so a second copy does not quietly win, it breaks the
 * build. That happened once during the 2026-09-08 keying pass: a hand-written batch and the codemod
 * both named the same key, and the shorter of two good sentences would have replaced the better one.
 */
const already = new Set(
  [...src.matchAll(/^\s*'([\w.]+)':/gm)].map((m) => m[1]),
);
const fresh = Object.entries(batch).filter(([key]) => !already.has(key));
const duplicates = Object.keys(batch).length - fresh.length;

const lines = [''];
if (comment) lines.push(`  // ${comment}`);
for (const [key, value] of fresh) {
  if (typeof value === 'object') {
    lines.push(`  '${key}': {`);
    for (const [form, text] of Object.entries(value)) lines.push(`    ${form}: ${escape(text)},`);
    lines.push('  },');
    continue;
  }
  const entry = `  '${key}': ${escape(value)},`;
  if (entry.length <= 100) lines.push(entry);
  else lines.push(`  '${key}':`, `    ${escape(value)},`);
}

const anchor = '} as const satisfies Record<TranslationKey, Message>;';
if (!src.includes(anchor)) throw new Error('closing anchor not found in ar.ts');
src = src.replace(anchor, lines.join(nl) + nl + anchor);
fs.writeFileSync(target, src);
console.log(
  `added ${fresh.length} Arabic entries` +
    (duplicates ? `, skipped ${duplicates} already present` : ''),
);
