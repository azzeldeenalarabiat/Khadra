/*
 * Lists every English key with no Arabic beside it, as a batch ready for `add-ar.js`.
 *
 * Development tooling. `ar.ts` is typed against `en.ts`, so a missing translation is a compile error
 * and cannot ship -- but during a keying pass the two go out of step by design, and this is what
 * says by how much and which ones.
 */
const fs = require('fs');

const ENTRY = /^\s*'([\w.]+)':\s*(?:'((?:[^'\\]|\\.)*)'|"((?:[^"\\]|\\.)*)")/gm;

function parse(path) {
  const src = fs.readFileSync(path, 'utf8');
  const map = new Map();
  for (const m of src.matchAll(ENTRY)) {
    map.set(m[1], (m[2] ?? m[3]).replace(/\\'/g, "'").replace(/\\"/g, '"'));
  }
  return map;
}

const en = parse('src/app/core/i18n/en.ts');
const ar = parse('src/app/core/i18n/ar.ts');

const missing = {};
for (const [key, value] of en) if (!ar.has(key)) missing[key] = value;

fs.writeFileSync('missing-ar.json', JSON.stringify(missing, null, 2), 'utf8');
console.log(`English ${en.size}, Arabic ${ar.size}, missing ${Object.keys(missing).length}`);
for (const [key, value] of Object.entries(missing)) {
  console.log('  ' + key.padEnd(44) + JSON.stringify(value));
}
