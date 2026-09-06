/*
 * Moves the copy inside a component's TypeScript behind a translation key.
 *
 * Development tooling: `node key-components.js <file.ts> <prefix>`.
 *
 * It only rewrites the properties that are known to carry user-facing copy — the confirmation
 * dialog's own shape, plus the label/placeholder/hint of a field — and only when the value is a
 * plain string. A template literal is left alone and reported: `${reference}` has to become a named
 * parameter, and where the value lands in the sentence differs between English and Arabic, so that
 * is a judgement rather than a substitution.
 */
const fs = require('fs');

const [, , file, prefix] = process.argv;
if (!file || !prefix) {
  console.error('usage: node key-components.js <file.ts> <key-prefix>');
  process.exit(1);
}

/** The properties that hold words a person reads. */
const COPY_PROPS = /\b(title|body|confirm|note|label|placeholder|hint|text|detail|summary)\s*:\s*'((?:[^'\\]|\\.)*)'/g;

let src = fs.readFileSync(file, 'utf8');
const entries = {};
const used = new Set();
const skipped = [];

function keyFor(text) {
  const words = text
    .toLowerCase()
    .replace(/['’]/g, '')
    .replace(/[^a-z0-9\s]/g, ' ')
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 4);
  let base = words[0] ?? 'text';
  for (let i = 1; i < words.length; i++) base += words[i][0].toUpperCase() + words[i].slice(1);
  base = base.slice(0, 34) || 'text';
  let key = `${prefix}.${base}`;
  let n = 2;
  while (used.has(key) && entries[key] !== text) key = `${prefix}.${base}${n++}`;
  used.add(key);
  return key;
}

function record(text) {
  const existing = Object.keys(entries).find((k) => entries[k] === text);
  if (existing) return existing;
  const key = keyFor(text);
  entries[key] = text;
  return key;
}

src = src.replace(COPY_PROPS, (whole, prop, value) => {
  const text = value.replace(/\\'/g, "'");
  // Needs at least two words of prose; `type: 'text'` and friends are not copy.
  if (!/[A-Za-z]{2,}\s+[A-Za-z]/.test(text)) return whole;
  const key = record(text);
  return `${prop}: this.t('${key}')`;
});

// Template literals in the same properties carry a value, so they need a named parameter.
for (const m of src.matchAll(/\b(title|body|confirm|note|label|placeholder|hint|text|detail)\s*:\s*`([^`]*)`/g)) {
  if (/[A-Za-z]{2,}\s+[A-Za-z]/.test(m[2])) skipped.push(`${m[1]}: \`${m[2].slice(0, 70)}\``);
}

fs.writeFileSync(file, src);
console.log(JSON.stringify(entries, null, 2));
if (skipped.length) {
  console.error(`\n-- ${skipped.length} interpolated string(s) left by hand in ${file}:`);
  for (const s of skipped) console.error('   ' + s);
}
