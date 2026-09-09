/*
 * Keys the module-level `describe(error)` helpers that map a server error CODE to a sentence.
 *
 * Development tooling: `node key-describe.js <file.ts> <key-prefix> [--write]`.
 *
 * These sit BELOW the class in almost every feature file, and `key-copy.js` deliberately refuses
 * them: a plain function has no `this`, so keying a string there produces `this.t(...)` in a context
 * with no injector and breaks the build. The fix is the same every time -- pass `t` in -- which is
 * exactly the shape a codemod is for.
 *
 * A mapping from the server's stable error CODE is the only part of a refusal that CAN be
 * translated. `Error.Message` and ProblemDetails `title` are English and stay English until the API
 * grows request localisation; they remain the last-resort fallback, which is why the default arm is
 * left alone.
 */
const fs = require('fs');

const [, , file, prefix, ...flags] = process.argv;
const write = flags.includes('--write');
if (!file || !prefix) {
  console.error('usage: node key-describe.js <file.ts> <key-prefix> [--write]');
  process.exit(1);
}

let src = fs.readFileSync(file, 'utf8');
const nl = src.includes('\r\n') ? '\r\n' : '\n';

const SIGNATURE = /function\s+(\w+)\s*\(\s*(\w+)\s*:\s*unknown\s*\)\s*:\s*string\s*\{/g;
const found = [...src.matchAll(SIGNATURE)];
if (found.length === 0) {
  console.log(`${file}: no module-level describe(error: unknown): string -- nothing to do`);
  process.exit(0);
}

const dictionary = fs.readFileSync('src/app/core/i18n/en.ts', 'utf8');
const existing = new Map();
for (const m of dictionary.matchAll(/^\s*'([\w.]+)':\s*(?:'((?:[^'\\]|\\.)*)'|"((?:[^"\\]|\\.)*)")/gm)) {
  existing.set((m[2] ?? m[3]).replace(/\\'/g, "'").replace(/\\"/g, '"'), m[1]);
}

const entries = {};
const used = new Set(existing.values());
let reused = 0;

function keyFor(text) {
  const already = existing.get(text);
  if (already) {
    reused++;
    return already;
  }
  for (const [seen, value] of Object.entries(entries)) if (value === text) return seen;
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
  while (used.has(key)) key = `${prefix}.${base}${n++}`;
  used.add(key);
  entries[key] = text;
  return key;
}

/** The body of the function whose opening brace is at `open`. */
function bodyRange(open) {
  let depth = 0;
  for (let i = open; i < src.length; i++) {
    if (src[i] === '{') depth++;
    else if (src[i] === '}' && --depth === 0) return [open + 1, i];
  }
  return [open + 1, src.length];
}

const names = [];
for (const m of found.reverse()) {
  const [signature, name] = m;
  names.push(name);
  const open = m.index + signature.length - 1;
  const [from, to] = bodyRange(open);
  const body = src.slice(from, to);

  const keyed = body.replace(/'((?:[^'\\\n]|\\.)*)'/g, (whole, raw) => {
    const text = raw.replace(/\\'/g, "'");
    // Prose only. An error code -- `dispute.already_open` -- is the thing being matched ON, not read.
    if (!/[A-Za-z]{3}\s+[A-Za-z]{2}/.test(text)) return whole;
    return `t('${keyFor(text)}')`;
  });

  src =
    src.slice(0, m.index) +
    signature.replace(/\)\s*:\s*string\s*\{$/, ', t: (key: TranslationKey) => string): string {') +
    keyed +
    src.slice(to);
}

// Every call site gains the argument, and the file gains the type import.
for (const name of names) {
  src = src.replace(new RegExp(`\\b${name}\\(([\\w.]+)\\)`, 'g'), (whole, arg) =>
    arg === 'key' ? whole : `${name}(${arg}, this.t)`,
  );
}
if (!src.includes("from '../../core/i18n/en'")) {
  src = src.replace(
    /(import \{ I18nService \} from '[^']*i18n\.service';)/,
    `$1${nl}import { TranslationKey } from '../../core/i18n/en';`,
  );
}

console.log(`${file}: ${Object.keys(entries).length} new, ${reused} reused, in ${names.join(', ')}`);
fs.writeFileSync('batch.json', JSON.stringify(entries, null, 2), 'utf8');
if (write) {
  fs.writeFileSync(file, src, 'utf8');
  console.log('wrote the file, and the English batch to batch.json');
}
