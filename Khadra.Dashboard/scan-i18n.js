/*
 * Finds user-facing English that is not yet behind a translation key.
 *
 * A development aid, not part of the build: `node scan-i18n.js` lists the files with work left,
 * `node scan-i18n.js --detail [path-fragment]` shows the strings themselves. It is deliberately
 * noisy rather than clever — it is easier to dismiss a false positive by eye than to discover a
 * missed sentence from an Arabic screen.
 */
const fs = require('fs');
const path = require('path');

function walk(dir, acc = []) {
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, entry.name);
    entry.isDirectory() ? walk(p, acc) : acc.push(p);
  }
  return acc;
}

const files = walk('src/app');
const report = [];

for (const file of files) {
  const rel = file.split(path.sep).join('/');
  if (rel.includes('/i18n/') || rel.endsWith('.spec.ts')) continue;

  let src = fs.readFileSync(file, 'utf8');
  const hits = [];

  if (file.endsWith('.html')) {
    src = src.replace(/<!--[\s\S]*?-->/g, '');
    // Control flow is not copy. `@if (x; as y) {`, `@for (a of b; track c) {`, `@else`, `}` and
    // interpolations all read as text between tags, and every one of them is a false positive.
    src = src
      .replace(/@(if|else if|for|switch|case|defer|placeholder|loading|error)\s*\([^{]*\)\s*\{/g, '')
      .replace(/@(else|default|empty|loading|placeholder)\s*\{/g, '')
      .replace(/@let\s+[^;]+;/g, '');

    for (const chunk of src.split(/<[^>]*>/g)) {
      const text = chunk
        .replace(/\{\{[^}]*\}\}/g, '')
        .replace(/[{}]/g, '')
        .trim();
      if (text.length > 1 && /[A-Za-z]{2,}/.test(text)) hits.push(text.slice(0, 70));
    }
    for (const m of src.matchAll(/\b(placeholder|title|aria-label|alt)="([^"{}]{2,})"/g)) {
      hits.push(`${m[1]}="${m[2].slice(0, 50)}"`);
    }
  } else if (file.endsWith('.ts')) {
    src = src.replace(/^\s*\/\/.*$/gm, '').replace(/\/\*[\s\S]*?\*\//g, '');
    for (const m of src.matchAll(/'([^'\n]{6,})'/g)) {
      const value = m[1];
      if (!/[A-Za-z]{3}\s+[A-Za-z]{2}/.test(value)) continue;
      if (value.includes('/') || value.includes('@')) continue;
      hits.push(value.slice(0, 70));
    }
  } else {
    continue;
  }

  if (hits.length) report.push({ count: hits.length, rel, hits });
}

report.sort((a, b) => b.count - a.count);
const total = report.reduce((n, r) => n + r.count, 0);

if (process.argv[2] === '--detail') {
  const filter = process.argv[3];
  for (const r of report) {
    if (filter && !r.rel.includes(filter)) continue;
    console.log(`\n### ${r.rel}  (${r.count})`);
    for (const h of r.hits) console.log('   ' + h);
  }
} else {
  for (const r of report) console.log(String(r.count).padStart(4), r.rel);
  console.log(`\nTOTAL ${total} in ${report.length} files`);
}
