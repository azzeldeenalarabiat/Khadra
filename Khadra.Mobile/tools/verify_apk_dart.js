// Proves an APK's compiled Dart is the code it is supposed to be, in EVERY ABI (pre-launch item 244).
//
// Why: on 10 Oct 2026 the Staging APK handed over as 1.4.0+7 carried, in every ABI, the compiled
// Dart of an 8 Oct build of another branch. Flutter had compiled the right code; Gradle's
// native-library merge and strip steps reused their outputs from the earlier build. The version,
// versionCode, signer and API address were all right, because none of them come from the Dart
// snapshot (lib/<abi>/libapp.so). This looks inside it.
//
// Two checks, each enough on its own to catch that APK:
//
//   --commit <sha>   The commit stamped at build time (--dart-define=KHADRA_BUILD_COMMIT, see
//                    lib/core/config/build_stamp.dart) must be in every ABI's libapp.so, in full.
//                    Exact, and the one to rely on.
//
//   --arb <app_en.arb> --since <previous release's app_en.arb>
//                    The English strings added or changed since the previous release must be in
//                    every ABI. An unused string is tree-shaken out of even a correct build, so the
//                    pass mark is 90% of that delta. For an APK built before the stamp existed.
//
// Usage (from Khadra.Mobile):
//   git show <previous-release>:Khadra.Mobile/lib/l10n/app_en.arb > /tmp/base_en.arb
//   node tools/verify_apk_dart.js build/app/outputs/flutter-apk/app-staging-release.apk \
//     --commit $(git rev-parse HEAD) --arb lib/l10n/app_en.arb --since /tmp/base_en.arb
//
// A bare libapp.so may be given instead of an APK. Exit 0 = every check passed in every ABI;
// 1 = a check failed; 2 = usage or read error.
const fs = require('fs');
const path = require('path');
const zlib = require('zlib');

function usage(message) {
  if (message) console.error(message);
  console.error('usage: verify_apk_dart.js <apk-or-libapp.so> [--commit <sha>] [--arb <app_en.arb> --since <base app_en.arb>]');
  process.exit(2);
}

const args = process.argv.slice(2);
const inputPath = args.shift();
const options = {};
while (args.length) {
  const flag = args.shift();
  if (!['--commit', '--arb', '--since'].includes(flag) || !args.length) usage('bad argument: ' + flag);
  options[flag.slice(2)] = args.shift();
}
if (!inputPath) usage();
if (!options.commit && !options.arb) usage('nothing to check: give --commit, or --arb with --since');
if (options.arb && !options.since) usage('--arb needs --since: the previous release to compare against');
if (options.commit && !/^[0-9a-f]{40}$/.test(options.commit)) usage('--commit must be a full 40-character lowercase SHA');

// Minimal ZIP reader: the central directory, then each lib/<abi>/libapp.so entry.
function zipEntries(buf) {
  let eocd = -1;
  for (let i = buf.length - 22; i >= Math.max(0, buf.length - 65557); i--) {
    if (buf.readUInt32LE(i) === 0x06054b50) {
      eocd = i;
      break;
    }
  }
  if (eocd < 0) throw new Error('not a zip');
  let off = buf.readUInt32LE(eocd + 16);
  const count = buf.readUInt16LE(eocd + 10);
  const out = [];
  for (let k = 0; k < count; k++) {
    const method = buf.readUInt16LE(off + 10);
    const csize = buf.readUInt32LE(off + 20);
    const nlen = buf.readUInt16LE(off + 28);
    const xlen = buf.readUInt16LE(off + 30);
    const clen = buf.readUInt16LE(off + 32);
    const local = buf.readUInt32LE(off + 42);
    out.push({ name: buf.toString('utf8', off + 46, off + 46 + nlen), method, csize, local });
    off += 46 + nlen + xlen + clen;
  }
  return out;
}

function zipRead(buf, entry) {
  const start = entry.local + 30 + buf.readUInt16LE(entry.local + 26) + buf.readUInt16LE(entry.local + 28);
  const data = buf.subarray(start, start + entry.csize);
  return entry.method === 0 ? data : zlib.inflateRawSync(data);
}

function readJson(file) {
  try {
    return JSON.parse(fs.readFileSync(file, 'utf8'));
  } catch (err) {
    usage('cannot read ' + file + ': ' + err.message);
  }
}

// The delta: strings added or changed since the base, long enough to be unambiguous, plain ASCII,
// and not ICU plural/select messages (those are compiled into fragments, not stored verbatim).
const probes = [];
if (options.arb) {
  const arb = readJson(options.arb);
  const base = readJson(options.since);
  for (const [key, value] of Object.entries(arb)) {
    if (key.startsWith('@') || typeof value !== 'string' || base[key] === value) continue;
    if (/,\s*(plural|select)\s*,/.test(value)) continue;
    const runs = value
      .split(/\{[^{}]*\}|\n/)
      .map((s) => s.trim())
      .filter((s) => s.length >= 12 && /^[\x20-\x7e]+$/.test(s));
    if (runs.length) probes.push({ key, text: runs.sort((a, b) => b.length - a.length)[0] });
  }
  if (!probes.length) usage('no strings changed between --since and --arb: compare against an earlier release');
}

let libs;
try {
  const input = fs.readFileSync(inputPath);
  libs = inputPath.toLowerCase().endsWith('.so')
    ? [{ name: path.basename(inputPath), bytes: input }]
    : zipEntries(input)
        .filter((e) => /^lib\/[^/]+\/libapp\.so$/.test(e.name))
        .map((e) => ({ name: e.name, bytes: zipRead(input, e) }));
} catch (err) {
  usage('cannot open ' + inputPath + ': ' + err.message);
}
if (!libs.length) usage('no lib/<abi>/libapp.so in ' + inputPath);

let failed = false;
for (const lib of libs) {
  if (options.commit) {
    const stamped = lib.bytes.includes(Buffer.from(options.commit, 'latin1'));
    failed ||= !stamped;
    console.log(`${stamped ? 'OK  ' : 'FAIL'} ${lib.name}: commit ${options.commit} ${stamped ? 'stamped' : 'NOT found'}`);
  }
  if (probes.length) {
    const missing = probes.filter((p) => !lib.bytes.includes(Buffer.from(p.text, 'latin1')));
    const present = probes.length - missing.length;
    const ok = present / probes.length >= 0.9;
    failed ||= !ok;
    console.log(`${ok ? 'OK  ' : 'FAIL'} ${lib.name}: ${present}/${probes.length} strings changed since the base release`);
    for (const m of missing.slice(0, 3)) console.log(`       missing ${m.key}: "${m.text.slice(0, 60)}"`);
  }
}
console.log(failed
  ? 'RESULT: FAIL. This APK does not carry the Dart it should. Do not hand it over.'
  : `RESULT: PASS in all ${libs.length} ABI(s).`);
process.exit(failed ? 1 : 0);
