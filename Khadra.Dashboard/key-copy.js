/*
 * Moves the remaining user-facing copy inside a component's TypeScript behind a translation key.
 *
 * Development tooling: `node key-copy.js <file.ts> <key-prefix> [--write]`.
 *
 * The successor to `key-components.js`, which only knew the confirmation dialog's own shape
 * (`title`, `body`, `confirm`, `label`, `placeholder`, `hint`). What it left behind was everything
 * assembled in any OTHER shape, and that turned out to be most of it: the `k`/`v` pairs of a
 * key-value row, the arms of a ternary, a `return` of a bare sentence, the values of a status map.
 * This finds those.
 *
 * WHAT IT WILL NOT TOUCH, and why each one would be a bug:
 *
 *   - Anything in a comment. Prose in a doc comment is prose, and keying it produces a key nobody
 *     reads and a file that no longer says why it does what it does.
 *   - Anything outside the class body: a module-level constant has no `this`.
 *   - A FIELD INITIALISER that is not inside an arrow function. `readonly x = 'Pending'` resolves
 *     once at construction, so keying it there freezes the language at load: switching with the
 *     screen open leaves the old words on it. That exact bug is recorded on pre-launch item 49,
 *     on the fleet filter chips. Inside `computed(() => ...)` the same string is safe, because the
 *     callback re-runs when the language signal changes.
 *   - Template literals. `${reference}` has to become a named parameter, and where the value lands
 *     in the sentence differs between English and Arabic. That is a judgement, not a substitution.
 *   - Strings that are not prose: identifiers, routes, icon names, CSS classes, enum members. The
 *     test is the same one `scan-i18n.js` uses -- two or more words -- plus an allow-list of the
 *     property names that carry single-word labels.
 *
 * It prints what it skipped, always. A codemod that silently declines is worse than one that
 * refuses loudly, because the sentence it walked past is invisible in the diff.
 */
const fs = require('fs');

const [, , file, prefix, ...flags] = process.argv;
const write = flags.includes('--write');
if (!file || !prefix) {
  console.error('usage: node key-copy.js <file.ts> <key-prefix> [--write]');
  process.exit(1);
}

const original = fs.readFileSync(file, 'utf8');

/**
 * Blanks every comment, keeping the file's length and line breaks so every offset still lines up
 * with the original. Replacing rather than removing is what lets the rewrite below splice into the
 * ORIGINAL text using offsets found in this one.
 */
function maskComments(src) {
  const chars = [...src];
  let mode = null; // 'line' | 'block' | 'single' | 'double' | 'template'
  for (let i = 0; i < chars.length; i++) {
    const c = chars[i];
    const next = chars[i + 1];
    if (mode === null) {
      if (c === '/' && next === '/') mode = 'line';
      else if (c === '/' && next === '*') mode = 'block';
      else if (c === "'") mode = 'single';
      else if (c === '"') mode = 'double';
      else if (c === '`') mode = 'template';
      if (mode === 'line' || mode === 'block') { chars[i] = ' '; chars[i + 1] = ' '; i++; }
      continue;
    }
    if (mode === 'line') { if (c === '\n') mode = null; else chars[i] = ' '; continue; }
    if (mode === 'block') {
      if (c === '*' && next === '/') { chars[i] = ' '; chars[i + 1] = ' '; i++; mode = null; }
      else if (c !== '\n') chars[i] = ' ';
      continue;
    }
    if (c === '\\') { i++; continue; }
    if ((mode === 'single' && c === "'") || (mode === 'double' && c === '"') || (mode === 'template' && c === '`')) mode = null;
  }
  return chars.join('');
}

const masked = maskComments(original);

/** Property names whose value is a label even when it is one word. */
const LABEL_PROPS = new Set(['k', 'label', 'title', 'name', 'caption', 'heading']);

/** Two or more words of letters: the same test scan-i18n.js applies. */
const isProse = (s) => /[A-Za-z]{3}\s+[A-Za-z]{2}/.test(s);

/** A path, an address, an interpolation, or a camelCase identifier -- not something a person reads. */
const isMachine = (s) =>
  s.includes('/') || s.includes('@') || s.includes('${') || /^[a-z]+([A-Z][a-z]*)+$/.test(s);

/**
 * The dictionary as it already stands, so an identical sentence reuses the key it already has.
 *
 * Without this the same word keyed on two screens becomes two entries with the same English and two
 * chances for the Arabic to drift apart -- and, in a file that has been through this codemod before,
 * a duplicate key in the object literal that TypeScript quietly resolves last-wins.
 */
const dictionary = fs.readFileSync('src/app/core/i18n/en.ts', 'utf8');
const existing = new Map();
for (const m of dictionary.matchAll(/^\s*'([\w.]+)':\s*(?:'((?:[^'\\]|\\.)*)'|"((?:[^"\\]|\\.)*)")/gm)) {
  existing.set((m[2] ?? m[3]).replace(/\\'/g, "'").replace(/\\"/g, '"'), m[1]);
}

const entries = {};
const used = new Set(existing.values());
const reused = [];
const skipped = [];

function keyFor(text) {
  const already = existing.get(text);
  if (already) {
    reused.push(already);
    return already;
  }
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
  // The same sentence twice in one file gets one key, not two.
  for (const [seen, value] of Object.entries(entries)) if (value === text) return seen;
  let key = `${prefix}.${base}`;
  let n = 2;
  while (used.has(key)) key = `${prefix}.${base}${n++}`;
  used.add(key);
  entries[key] = text;
  return key;
}

const classAt = masked.search(/export\s+(?:default\s+)?class\b/);
if (classAt < 0) {
  console.error('no class in this file; nothing to key');
  process.exit(1);
}

/**
 * Where the class body ends.
 *
 * "After the class starts" is not the same as "inside the class", and the difference is not
 * theoretical: these files habitually keep a module-level `describe(error)` helper BELOW the class,
 * and keying a string there produced `this.t(...)` in a plain function -- which has no `this`, and
 * broke the build. Such a helper takes `t` as a parameter instead; the codemod leaves it alone and
 * says so.
 */
const classEnd = (() => {
  const open = masked.indexOf('{', classAt);
  if (open < 0) return masked.length;
  let depth = 0;
  for (let i = open; i < masked.length; i++) {
    if (masked[i] === '{') depth++;
    else if (masked[i] === '}' && --depth === 0) return i;
  }
  return masked.length;
})();

/**
 * Whether a string at this offset re-evaluates when the language changes.
 *
 * The dangerous shape is a class FIELD whose value is the string itself. The safe shape is the same
 * string inside an arrow function -- a `computed`, a `signal` callback, an event handler -- or in a
 * method body. So: find where the enclosing class member starts, and ask whether an arrow opened
 * between there and here.
 */
function reEvaluates(at) {
  const memberStart = Math.max(
    masked.lastIndexOf('\n  protected ', at),
    masked.lastIndexOf('\n  private ', at),
    masked.lastIndexOf('\n  public ', at),
    masked.lastIndexOf('\n  readonly ', at),
    masked.lastIndexOf('\n  constructor', at),
  );
  if (memberStart < 0) return true;
  const member = masked.slice(memberStart, at);
  // A method -- `name(args) {` -- or any arrow between the member's start and this string.
  return member.includes('=>') || /\)\s*(:[^=\n]*)?\s*\{/.test(member);
}

const RE = /(?:(\w+)\s*:\s*)?'((?:[^'\\\n]|\\.)*)'/g;
let out = '';
let cursor = 0;
let m;
while ((m = RE.exec(masked)) !== null) {
  const [whole, prop, raw] = m;
  const at = m.index;
  const text = raw.replace(/\\'/g, "'");

  const inClass = at > classAt && at < classEnd;
  const worthKeying =
    text.length > 1 && !isMachine(text) && (isProse(text) || (prop && LABEL_PROPS.has(prop)));

  if (!worthKeying) continue;

  if (!inClass) {
    skipped.push({ text, why: 'outside the class body: a module function has no `this`' });
    continue;
  }

  if (!reEvaluates(at)) {
    skipped.push({ text, why: 'class field: keying it here would freeze the language at construction' });
    continue;
  }

  out += original.slice(cursor, at) + (prop ? `${prop}: ` : '') + `this.t('${keyFor(text)}')`;
  cursor = at + whole.length;
}
out += original.slice(cursor);

console.log(
  `${file}: ${Object.keys(entries).length} new, ${reused.length} reused, ${skipped.length} skipped`,
);
for (const s of skipped) console.log(`   SKIP  ${s.text.slice(0, 60)}  -- ${s.why}`);

fs.writeFileSync('batch.json', JSON.stringify(entries, null, 2), 'utf8');
if (write) {
  fs.writeFileSync(file, out, 'utf8');
  console.log('wrote the file, and the English batch to batch.json');
} else {
  console.log('dry run; the English batch is in batch.json. Re-run with --write to apply.');
}
