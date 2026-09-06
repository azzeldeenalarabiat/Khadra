/*
 * Moves literal copy out of a template and behind a translation key.
 *
 * Development tooling, run once per screen: `node key-templates.js <file.html> <prefix>`.
 *
 * It only touches nodes it can rewrite safely — a text node that is ENTIRELY literal, and the four
 * attributes that carry copy. A node mixing literal words with an interpolation is left alone and
 * reported, because splitting one sentence around a value is a judgement about word order that
 * differs between English and Arabic, and getting it wrong silently is worse than doing it by hand.
 *
 * Emits the English entries as JSON on stdout so they can be pasted into en.ts.
 */
const fs = require('fs');

const [, , file, prefix] = process.argv;
if (!file || !prefix) {
  console.error('usage: node key-templates.js <file.html> <key-prefix>');
  process.exit(1);
}

let src = fs.readFileSync(file, 'utf8');
const entries = {};
const skipped = [];
const used = new Set();

/**
 * Splits a template into tags and the text between them.
 *
 * Written by hand rather than with `split(/(<[^>]*>)/)`, because that regex ends a tag at the first
 * `>` it sees — including one inside an attribute value. `[disabled]="page() >= totalPages()"` cut a
 * button in half and swallowed the markup after it, which the codemod then happily rewrote.
 */
function tokenize(html) {
  const out = [];
  let i = 0;
  let text = '';
  while (i < html.length) {
    if (html[i] !== '<') {
      text += html[i++];
      continue;
    }
    if (text) {
      out.push({ tag: false, value: text });
      text = '';
    }
    // A comment is one opaque token. Treating it as markup let a `<button>` written INSIDE a
    // comment end the comment early, after which the codemod rewrote the prose that followed and
    // swallowed the `-->` with it.
    if (html.startsWith('<!--', i)) {
      const end = html.indexOf('-->', i);
      const stop = end === -1 ? html.length : end + 3;
      out.push({ tag: true, value: html.slice(i, stop) });
      i = stop;
      continue;
    }
    let j = i + 1;
    let quote = null;
    while (j < html.length) {
      const c = html[j];
      if (quote) {
        if (c === quote) quote = null;
      } else if (c === '"' || c === "'") {
        quote = c;
      } else if (c === '>') {
        break;
      }
      j++;
    }
    out.push({ tag: true, value: html.slice(i, j + 1) });
    i = j + 1;
  }
  if (text) out.push({ tag: false, value: text });
  return out;
}

/** A short, stable, readable key from the sentence itself. */
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

// --- 1. Attributes that carry copy -------------------------------------------------------------
src = src.replace(/\b(placeholder|title|aria-label|alt)="([^"{}]*[A-Za-z]{2}[^"{}]*)"/g, (m, attr, value) => {
  const text = value.trim();
  if (!/[A-Za-z]{2}/.test(text)) return m;
  const key = record(text);
  const bound = attr === 'aria-label' ? '[attr.aria-label]' : `[${attr}]`;
  return `${bound}="t('${key}')"`;
});

// --- 2. Text nodes -----------------------------------------------------------------------------
const tokens = tokenize(src);
const parts = tokens.map((t) => t.value);
for (let i = 0; i < tokens.length; i++) {
  if (tokens[i].tag) continue;
  const part = tokens[i].value;

  // Control-flow braces and interpolations are not copy.
  const stripped = part
    .replace(/@(if|else if|for|switch|case|defer|placeholder|loading|error)\s*\([^{]*\)\s*\{/g, '')
    .replace(/@(else|default|empty)\s*\{/g, '')
    .replace(/[{}]/g, '');

  if (!/[A-Za-z]{2}/.test(stripped)) continue;

  // A text node is rarely all copy: `} @else { Nothing needs attention }` is two control-flow
  // tokens around one sentence. Split the node on the tokens and interpolations, and key only the
  // literal runs between them — skipping the whole node would leave that sentence in English.
  const SEGMENT =
    /(\{\{[\s\S]*?\}\}|@(?:if|else if|for|switch|case|defer|placeholder|loading|error)\s*\([^{]*\)\s*\{|@(?:else|default|empty|loading|placeholder)\s*\{|[{}])/g;

  const pieces = part.split(SEGMENT).filter((p) => p !== undefined);
  let changed = false;
  const rebuilt = pieces.map((piece) => {
    if (SEGMENT.test(piece)) {
      SEGMENT.lastIndex = 0;
      return piece;
    }
    SEGMENT.lastIndex = 0;
    const text = piece.trim();
    if (text.length < 2 || !/[A-Za-z]{2}/.test(text)) return piece;

    // A run that is only punctuation around a value ("· ", " of ") is a sentence built around an
    // interpolation. Word order differs in Arabic, so those are left for a human to phrase.
    if (!/[A-Za-z]{2,}\s|[A-Za-z]{3,}/.test(text)) return piece;

    const key = record(text.replace(/\s+/g, ' '));
    changed = true;
    const lead = piece.match(/^\s*/)[0];
    const tail = piece.match(/\s*$/)[0];
    return `${lead}{{ t('${key}') }}${tail}`;
  });

  if (changed) {
    parts[i] = rebuilt.join('');
  } else if (/[A-Za-z]{2}/.test(part.replace(/\{\{[\s\S]*?\}\}/g, ''))) {
    skipped.push(part.trim().replace(/\s+/g, ' ').slice(0, 90));
  }
  continue;
}
src = parts.join('');

fs.writeFileSync(file, src);

console.log(JSON.stringify(entries, null, 2));
if (skipped.length) {
  console.error(`\n-- ${skipped.length} node(s) left for manual handling in ${file}:`);
  for (const s of skipped) console.error('   ' + s);
}
