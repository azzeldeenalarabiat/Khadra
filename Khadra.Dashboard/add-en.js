/*
 * Appends a batch of English entries to en.ts, in the file's own formatting.
 * Development tooling: `node add-en.js batch.json "# comment"`.
 */
const fs = require('fs');

const [, , batchFile, comment] = process.argv;
const batch = JSON.parse(fs.readFileSync(batchFile, 'utf8'));
const target = 'src/app/core/i18n/en.ts';
let src = fs.readFileSync(target, 'utf8');
const nl = src.includes('\r\n') ? '\r\n' : '\n';

const escape = (s) => (s.includes("'") ? `"${s.replace(/"/g, '\\"')}"` : `'${s}'`);

const lines = [''];
if (comment) lines.push(`  // ${comment}`);
for (const [key, value] of Object.entries(batch)) {
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

const anchor = '} as const satisfies Record<string, Message>;';
if (!src.includes(anchor)) throw new Error('closing anchor not found in en.ts');
src = src.replace(anchor, lines.join(nl) + nl + anchor);
fs.writeFileSync(target, src);
console.log(`added ${Object.keys(batch).length} English entries`);
