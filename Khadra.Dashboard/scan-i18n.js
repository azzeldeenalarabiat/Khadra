/*
 * Finds user-facing copy that bypasses the translation system.
 *
 *   npm run i18n:check                     exits 1 when anything is found outside the allowlist below
 *   node scan-i18n.js --detail [fragment]  lists every hit, allowlisted ones included
 *
 * It parses every template with Angular's own template parser and every TypeScript file with the
 * TypeScript compiler. The regex scanner it replaced could not see a single word ('All'), a backtick
 * sentence (`none in ${h}h`), a string inside `{{ a ? 'x' : 'y' }}`, or `toLocaleString('en-GB')`,
 * and reported 75 leftovers on a console that still had several hundred — so an Arabic Dealer console
 * shipped reading "No bookings yet", "Pending" and "none in 48h".
 *
 * A hit is:
 * - in a template: a text node with letters; a string literal inside an interpolation, a bound
 *   attribute or an event handler that is not a translation key, a comparison operand or an index; a
 *   placeholder / title / aria-label / alt attribute with letters;
 * - in TypeScript: a string or template literal with letters that is not a key, a path, an identifier,
 *   a class list, markup, a comparison operand, an object key, a type, an import, a decorator
 *   argument or an argument of an API that takes machine values; a lower-case single word only where
 *   it is chosen as copy (interpolated into a sentence, or the other branch of a ternary is copy);
 * - anywhere outside core/i18n: `toLocaleString`, `toLocaleDateString`, `toLocaleTimeString`,
 *   `new Intl.*`, and `toFixed` in a component, which print in a fixed language.
 *
 * Single capitalised words are hits on purpose: an enum value shown as a label is exactly the defect
 * this exists to find, and a genuine comparison against one is excluded by its position.
 */
'use strict';

const fs = require('fs');
const path = require('path');
const { pathToFileURL } = require('url');

const ROOT = __dirname;
const APP = path.join(ROOT, 'src', 'app');

/**
 * Latin text that is correct on an Arabic screen. Matched by file suffix and exact text.
 *
 * Every entry is a claim that someone reading Arabic should see these characters, so each carries its
 * reason. Keep it short.
 */
const ALLOW = [
  // The currency code typeset in a <small> beside an amount already formatted by `| money: 'amount'`,
  // the layout MoneyPipe documents. The pair sits in one .ltr run, so it reads "1,234.000 JOD" in
  // both languages, like every other amount.
  {
    file: 'features/dealer/dealer-reports.component.html',
    text: '{{ …currency }}',
    reason: 'currency code typeset beside | money: amount, inside one .ltr run',
  },
  // OpenStreetMap's tile licence requires this attribution, in this form, on the map.
  {
    file: 'shared/map/map.component.ts',
    text: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors',
    reason: 'licence attribution required by the tile provider',
  },
  // Manufacturer names, which are spelled the same on an Arabic screen as on an English one. The
  // list itself is a separate defect, recorded in docs/pre-launch-checklist.md: it is nine makes
  // typed into the wizard rather than a vocabulary the platform serves.
  {
    file: 'features/fleet/vehicle-wizard.component.ts',
    texts: ['Toyota', 'Hyundai', 'Kia', 'Nissan', 'Mitsubishi', 'Chevrolet', 'Honda'],
    reason: 'manufacturer names, identical in both languages',
  },
  // The endpoint a dealer is asked to quote to support when the gate cannot answer.
  {
    file: 'features/dealer/dealer-gate.component.html',
    text: 'GET /api/v1/dealers/me',
    reason: 'an HTTP endpoint, quoted to support',
  },
  // The shape of a Jordanian mobile number, typed in Latin digits whatever the console language.
  {
    file: 'features/auth/register-dealer.component.html',
    text: 'placeholder="07XXXXXXXX"',
    reason: 'phone number format hint, Latin by design',
  },
];

const LETTERS = /[A-Za-z]/;
const WORDS = /[A-Za-z]{2,}/;

const isPath = (s) =>
  /^(\/|\.\.?\/|https?:|mailto:|tel:|data:|blob:|#)/.test(s) || (/\//.test(s) && !/\s/.test(s));
const isKey = (s) => /^[a-z][A-Za-z0-9]*(\.[A-Za-z0-9_]+)+$/.test(s);
const isIdentifier = (s) =>
  /^[a-z0-9]+([-_:.][a-z0-9]+)*[-_:]?$/.test(s) ||
  /^[a-z]+[A-Z][A-Za-z0-9]*$/.test(s) ||
  /^[A-Z][a-z0-9]+([A-Z][a-z0-9]*)+$/.test(s) ||
  /^(X-)?[A-Z][a-zA-Z]*(-[A-Z][a-zA-Z]*)+$/.test(s) ||
  /^[0-9.]+(px|rem|em|%|ms|s)$/.test(s);
const isClassList = (s) =>
  /^[a-z0-9 _-]+$/.test(s) &&
  s
    .split(/\s+/)
    .filter(Boolean)
    .some((token) => token.includes('-'));
/**
 * Markup built in code (a map pin's inner HTML, an SVG's attributes carried onto the next line) is
 * structure, not copy.
 */
const isMarkup = (s) => /^\s*</.test(s) || /^\s*([\w:-]+="[^"]*"\s*)+\/?>?\s*$/.test(s);
/** A route parameter placeholder, as in `/dealers/:id`. */
const isRouteParameter = (s) => /^:[a-z][A-Za-z0-9]*$/.test(s);

/** What a string literal is, given where it sits. Null when it is not copy. */
function stringKind(text, copyContext) {
  const s = text.trim();
  if (!s || !LETTERS.test(s) || isMarkup(s)) return null;
  if (/\s/.test(s)) {
    if (!WORDS.test(s) || isPath(s) || isClassList(s)) return null;
    return 'sentence';
  }
  if (isPath(s) || isKey(s) || isRouteParameter(s)) return null;
  if (/^[a-z]+$/.test(s)) return copyContext ? 'word' : null;
  if (/^[A-Z][a-z]+$/.test(s)) return 'word';
  if (/^[A-Z]{2,5}$/.test(s)) return copyContext ? 'code' : null;
  if (isIdentifier(s)) return null;
  // "Working…", "No-show", "Attached:" — letters with the punctuation copy carries.
  return WORDS.test(s) && /[…:!?]|^[A-Z][a-z]+-[a-z]+$/.test(s) ? 'word' : null;
}

/** A template literal is copy unless every literal part is a path, an id or a class fragment. */
function templateKind(parts) {
  const text = parts.join(' ');
  if (!LETTERS.test(text)) return null;
  const pieces = parts.map((part) => part.trim()).filter(Boolean);
  if (pieces.length === 0) return null;
  const machine = (piece) =>
    isPath(piece) ||
    (/^[a-z0-9:_./-]+$/i.test(piece) && /[-:_./]/.test(piece)) ||
    isClassList(piece) ||
    isMarkup(piece);
  return pieces.every(machine) ? null : 'template';
}

async function main() {
  const ts = require(require.resolve('typescript', { paths: [ROOT] }));
  const ng = await import(
    pathToFileURL(require.resolve('@angular/compiler', { paths: [ROOT] })).href
  );

  const walk = (dir, acc = []) => {
    for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
      const full = path.join(dir, entry.name);
      if (entry.isDirectory()) walk(full, acc);
      else acc.push(full);
    }
    return acc;
  };
  const rel = (file) => path.relative(APP, file).split(path.sep).join('/');

  const files = walk(APP).filter((file) => {
    const name = rel(file);
    return (
      /\.(ts|html)$/.test(name) &&
      !name.endsWith('.spec.ts') &&
      !name.startsWith('core/i18n/') &&
      name !== 'shared/icon/icon-paths.ts'
    );
  });

  const hits = [];
  const add = (file, line, kind, text) =>
    hits.push({ file: rel(file), line, kind, text: String(text).replace(/\s+/g, ' ').trim() });

  // ── TypeScript ──────────────────────────────────────────────────────────────────────────────
  const KEY_CALLEES = new Set(['t', 'statusLabel', 'enumLabel', 'title']);
  /**
   * APIs whose STRING arguments are machine values: a selector, a storage key, a URL, a regex
   * replacement, an event name. Only their literal arguments are skipped. Every other argument is
   * still read — a callback especially: `rows.map((row) => ({ status: 'Provided' }))` and
   * `problem.set('Could not save')` are copy, and skipping whole argument lists hid them (Wave Two,
   * 2026-09-17: a timeline built inside `.map` printed `toLocaleString('en-GB')` unseen).
   */
  const MACHINE_CALLEES = new Set([
    'inject', 'querySelector', 'querySelectorAll', 'getItem', 'setItem', 'removeItem',
    'setAttribute', 'getAttribute', 'removeAttribute', 'addEventListener', 'removeEventListener',
    'createElement', 'matchMedia', 'require', 'InjectionToken', 'navigate', 'navigateByUrl', 'get',
    'post', 'put', 'patch', 'delete', 'set', 'has', 'append', 'httpResource', 'getElementById',
    'closest', 'contains', 'toggle', 'add', 'remove', 'replace', 'split', 'join', 'startsWith',
    'endsWith', 'includes', 'indexOf', 'padStart', 'padEnd', 'match', 'matchAll', 'test', 'RegExp',
    'Symbol', 'Date', 'parse', 'localeCompare', 'getPropertyValue', 'setProperty', 'CustomEvent',
    'Blob', 'createObjectURL', 'open', 'scrollIntoView', 'focus', 'bypassSecurityTrustHtml',
    'bypassSecurityTrustResourceUrl', 'setTimeout', 'find', 'filter', 'some', 'every', 'map', 'Set',
    // The fleet's status actions ('Publish', 'Hide', 'SendToMaintenance') are the API's own verbs.
    'changeStatus',
  ]);
  const literalArgument = (node) =>
    ts.isStringLiteralLike(node) || ts.isTemplateExpression(node) || ts.isRegularExpressionLiteral(node);
  /** Properties whose value is read by a person. A lower-case single word here is copy. */
  const COPY_PROPS = new Set([
    'label', 'title', 'desc', 'description', 'note', 'text', 'message', 'body', 'main', 'entity',
    'when', 'meta', 'v', 'k', 'hint', 'placeholder', 'sub', 'caption', 'heading', 'subtitle',
    'summary', 'detail', 'tooltip', 'headline', 'confirm', 'figure', 'tag', 'badge', 'empty', 'unit',
    'severity', 'sla', 'ts',
  ]);
  /**
   * Properties whose value is a MACHINE value, whatever it looks like: the filter a chip sends, the
   * action a row posts, the field name a dialog submits. A capitalised word here is an API value —
   * `{ key: 'Active', label: 'fleetList.listed' }` — and the label beside it is the copy.
   */
  const MACHINE_PROPS = new Set([
    'key', 'value', 'action', 'kind', 'type', 'id', 'name', 'code', 'route', 'param',
    'field', 'icon', 'tone', 'scope', 'method', 'format', 'target', 'day', 'transmission', 'fuelType',
    'fuelPolicy', 'pickupMethod', 'party',
  ]);
  // Deliberately NOT `status`: a document tile's `status: 'Provided'` and a handover row's
  // `status: 'Active'` are words a person reads, and those are exactly the ones this scanner exists
  // to catch. A status VALUE sent to the server travels as `key` or in a typed list.
  const calleeName = (expr) =>
    ts.isIdentifier(expr) ? expr.text : ts.isPropertyAccessExpression(expr) ? expr.name.text : '';
  /**
   * A module constant holding nothing but bare strings — `const QUEUES = ['Open', 'Resolved']`, the
   * API's own day names — is a list of VALUES. Copy is never written this way: it is keys, which this
   * scanner already knows, or objects with a label beside the value.
   */
  const machineValueList = (node) => {
    if (!ts.isVariableDeclaration(node) || !ts.isIdentifier(node.name)) return false;
    if (!/^[A-Z][A-Z0-9_]*$/.test(node.name.text)) return false;
    const initializer =
      node.initializer && ts.isAsExpression(node.initializer)
        ? node.initializer.expression
        : node.initializer;
    return (
      !!initializer &&
      ts.isArrayLiteralExpression(initializer) &&
      initializer.elements.length > 0 &&
      initializer.elements.every(
        (element) =>
          ts.isStringLiteralLike(element) ||
          element.kind === ts.SyntaxKind.NullKeyword ||
          (ts.isIdentifier(element) && element.text === 'undefined'),
      )
    );
  };

  const scanTs = (file, source) => {
    const sf = ts.createSourceFile(file, source, ts.ScriptTarget.Latest, true, ts.ScriptKind.TS);
    const line = (node) => sf.getLineAndCharacterOfPosition(node.getStart(sf)).line + 1;
    const component = /^(features|layout|shared)\//.test(rel(file));

    /** Whether an expression is visibly copy: a key lookup, a sentence, a title word, a copy template. */
    const copyish = (node) =>
      (ts.isCallExpression(node) && KEY_CALLEES.has(calleeName(node.expression))) ||
      ((ts.isStringLiteral(node) || ts.isNoSubstitutionTemplateLiteral(node)) &&
        stringKind(node.text, false) !== null) ||
      (ts.isTemplateExpression(node) &&
        templateKind([node.head.text, ...node.templateSpans.map((span) => span.literal.text)]) !==
          null) ||
      (ts.isParenthesizedExpression(node) && copyish(node.expression)) ||
      (ts.isConditionalExpression(node) && (copyish(node.whenTrue) || copyish(node.whenFalse)));

    const functionLike = (node) =>
      ts.isArrowFunction(node) ||
      ts.isFunctionExpression(node) ||
      ts.isFunctionDeclaration(node) ||
      ts.isMethodDeclaration(node) ||
      ts.isGetAccessorDeclaration(node) ||
      ts.isConstructorDeclaration(node);

    /**
     * `machine` is set while reading a value handed to a machine API — `status.set(key === 'active'
     * ? 'Active' : null)` sends a filter to the server — and cleared again inside any function, whose
     * body is ordinary code. In it, literals are not copy; calls are still checked.
     */
    const visit = (node, copy, machine = false) => {
      if (!node) return;
      if (
        ts.isImportDeclaration(node) ||
        ts.isExportDeclaration(node) ||
        ts.isDecorator(node) ||
        ts.isInterfaceDeclaration(node) ||
        ts.isTypeAliasDeclaration(node) ||
        ts.isEnumDeclaration(node) ||
        ts.isThrowStatement(node) ||
        (ts.isTypeNode(node) && !ts.isExpressionWithTypeArguments(node))
      ) {
        return;
      }
      if (machine && functionLike(node)) {
        visit(node, copy, false);
        return;
      }

      if (ts.isCallExpression(node) || ts.isNewExpression(node)) {
        const name = calleeName(node.expression);
        const full = node.expression.getText(sf);
        if (/^console\./.test(full) || /Error$/.test(name)) return;
        if (/^toLocale(Date|Time)?String$/.test(name)) add(file, line(node), 'toLocale', node.getText(sf));
        if (/^Intl\./.test(full)) add(file, line(node), 'Intl', node.getText(sf));
        if (component && name === 'toFixed') add(file, line(node), 'toFixed', node.getText(sf));
        // `['Requested', 'Approved'].includes(status)` is a membership test, not a list of labels.
        const membership =
          (name === 'includes' || name === 'indexOf') &&
          ts.isPropertyAccessExpression(node.expression) &&
          ts.isArrayLiteralExpression(node.expression.expression);
        if (membership) {
          visit(node.expression.expression, false, true);
        } else {
          visit(node.expression, false, machine);
        }
        (node.arguments ?? []).forEach((arg, index) => {
          if (index === 0 && KEY_CALLEES.has(name)) return;
          if (/^Intl\./.test(full) || /^toLocale/.test(name)) return;
          if (MACHINE_CALLEES.has(name)) {
            if (literalArgument(arg)) return;
            visit(arg, false, !functionLike(arg));
            return;
          }
          visit(arg, false, machine);
        });
        return;
      }

      if (ts.isBinaryExpression(node)) {
        const comparison = [
          ts.SyntaxKind.EqualsEqualsEqualsToken,
          ts.SyntaxKind.ExclamationEqualsEqualsToken,
          ts.SyntaxKind.EqualsEqualsToken,
          ts.SyntaxKind.ExclamationEqualsToken,
          ts.SyntaxKind.InKeyword,
        ].includes(node.operatorToken.kind);
        if (comparison) {
          for (const side of [node.left, node.right]) {
            if (!ts.isStringLiteralLike(side)) visit(side, false, machine);
          }
          return;
        }
      }

      if (ts.isCaseClause(node)) {
        node.statements.forEach((statement) => visit(statement, false, machine));
        return;
      }
      if (ts.isElementAccessExpression(node)) {
        visit(node.expression, false, machine);
        if (!ts.isStringLiteralLike(node.argumentExpression)) visit(node.argumentExpression, false, machine);
        return;
      }
      if (ts.isPropertyAssignment(node)) {
        const prop = ts.isIdentifier(node.name) || ts.isStringLiteral(node.name) ? node.name.text : '';
        visit(node.initializer, COPY_PROPS.has(prop), machine || MACHINE_PROPS.has(prop));
        return;
      }
      if (machineValueList(node)) {
        visit(node.initializer, false, true);
        return;
      }
      if (ts.isConditionalExpression(node)) {
        // A branch is copy when it lands in copy, or when the other branch visibly is copy: a tone
        // chosen between 'bad' and 'ok' is not, a word chosen beside a t() call is.
        visit(node.condition, false, machine);
        const branches = copy || copyish(node.whenTrue) || copyish(node.whenFalse);
        visit(node.whenTrue, branches, machine);
        visit(node.whenFalse, branches, machine);
        return;
      }
      if (ts.isArrowFunction(node) && !ts.isBlock(node.body)) {
        visit(node.body, copy);
        return;
      }
      if (ts.isStringLiteral(node) || ts.isNoSubstitutionTemplateLiteral(node)) {
        if (machine) return;
        const kind = stringKind(node.text, copy);
        if (kind) add(file, line(node), kind, node.text);
        return;
      }
      if (ts.isTemplateExpression(node)) {
        const parts = [node.head.text, ...node.templateSpans.map((span) => span.literal.text)];
        const kind = machine ? null : templateKind(parts);
        if (kind) add(file, line(node), kind, parts.join('${…}'));
        // `${value.amount} ${value.currency}`: money glued together by hand, outside FormatService —
        // no scale, no Latin-digit pin, no bidi isolate, and the road "JOD 0−" came in by.
        if (
          !machine &&
          node.templateSpans.some((span) => /(^|\.)(currency|cur)$/.test(span.expression.getText(sf)))
        ) {
          add(file, line(node), 'money', node.getText(sf));
        }
        // Whatever is interpolated into a sentence is read as part of it.
        node.templateSpans.forEach((span) => visit(span.expression, kind !== null, machine));
        return;
      }
      ts.forEachChild(node, (child) => visit(child, copy, machine));
    };
    visit(sf, false);
  };

  // ── Templates ───────────────────────────────────────────────────────────────────────────────
  const COPY_ATTRS = new Set([
    'placeholder', 'title', 'aria-label', 'alt', 'label', 'aria-description', 'aria-placeholder',
    'aria-valuetext', 'aria-roledescription',
  ]);
  const MACHINE_BINDINGS =
    /^(class|style|routerLink|routerLinkActive|queryParams|queryParamsHandling|fragment|src|href|id|type|name|value|formControlName|ngClass|ngStyle|tone|icon|variant|for)$|^(class|style)\.|^attr\.(class|style|href|role|for|tabindex|inputmode|autocomplete|data-.*|aria-(selected|busy|current|expanded|pressed|checked|hidden|controls|live|modal|haspopup))$/;
  const SKIP_KEYS = new Set([
    'sourceSpan', 'startSourceSpan', 'endSourceSpan', 'keySpan', 'valueSpan', 'nameSpan', 'span',
    'i18n', 'handlerSpan', 'location', 'errors', 'mainBlockSpan',
  ]);

  const scanHtml = (file, source) => {
    const parsed = ng.parseTemplate(source, file, {
      preserveWhitespaces: false,
      enableBlockSyntax: true,
      enableLetSyntax: true,
    });
    for (const error of parsed.errors ?? []) add(file, 0, 'parse-error', error.msg ?? String(error));
    const lineAt = (offset) => source.slice(0, offset).split('\n').length;
    const seen = new WeakSet();

    const expression = (node, parents) => {
      if (!node || typeof node !== 'object' || seen.has(node)) return;
      seen.add(node);
      const parent = parents[parents.length - 1];
      // `{{ row.totalPrice }} {{ row.currency }}`: a currency code printed on its own beside a raw
      // amount, instead of one `| money` or FormatService call that formats and isolates the pair.
      if (
        node instanceof ng.PropertyRead &&
        node.name === 'currency' &&
        !parents.some((ancestor) => ancestor instanceof ng.BindingPipe || ancestor instanceof ng.Call)
      ) {
        add(file, lineAt(node.sourceSpan?.start ?? 0), 'money', `{{ …${node.name} }}`);
      }
      if (node instanceof ng.LiteralPrimitive && typeof node.value === 'string') {
        const receiver = parent instanceof ng.Call ? parent.receiver?.name ?? '' : '';
        if (parent instanceof ng.Call && parent.args?.[0] === node && KEY_CALLEES.has(receiver)) return;
        if (parent instanceof ng.Binary && ['===', '!==', '==', '!='].includes(parent.operation)) return;
        if (parent instanceof ng.KeyedRead && parent.key === node) return;
        // A branch of a ternary rendered into the page is copy, so even a lower-case word counts.
        const branch =
          parent instanceof ng.Conditional && (parent.trueExp === node || parent.falseExp === node);
        const kind = stringKind(node.value, branch);
        if (kind) add(file, lineAt(node.sourceSpan?.start ?? 0), kind, node.value);
        return;
      }
      // `{{ latitude().toFixed(4) }}` prints a number in one fixed shape, outside FormatService:
      // no Latin-digit pin, no isolate. The TypeScript half of this scanner has always refused it.
      if (node instanceof ng.Call && node.receiver instanceof ng.PropertyRead && node.receiver.name === 'toFixed') {
        add(file, lineAt(node.sourceSpan?.start ?? 0), 'toFixed', '{{ ….toFixed(…) }}');
      }
      if (ng.TemplateLiteralElement && node instanceof ng.TemplateLiteralElement) {
        if (WORDS.test(node.text ?? '')) add(file, lineAt(node.sourceSpan?.start ?? 0), 'template', node.text);
        return;
      }
      for (const [key, value] of Object.entries(node)) {
        if (SKIP_KEYS.has(key)) continue;
        if (Array.isArray(value)) value.forEach((item) => expression(item, [...parents, node]));
        else if (value && typeof value === 'object') expression(value, [...parents, node]);
      }
    };

    const template = (node) => {
      if (!node || typeof node !== 'object' || seen.has(node)) return;
      seen.add(node);
      if (node instanceof ng.TmplAstText) {
        if (WORDS.test(node.value)) add(file, node.sourceSpan.start.line + 1, 'text', node.value);
        return;
      }
      if (node instanceof ng.TmplAstTextAttribute) {
        if (COPY_ATTRS.has(node.name) && WORDS.test(node.value ?? '')) {
          add(file, node.sourceSpan.start.line + 1, 'attr', `${node.name}="${node.value}"`);
        }
        return;
      }
      if (node instanceof ng.TmplAstBoundText) {
        // The words AROUND the interpolations — `{{ radius() }} km`, `{{ a }} of {{ b }}` — live in
        // the interpolation's own string list, not in a node, and were read by nothing until Wave Two
        // (2026-09-17).
        const ast = node.value?.ast ?? node.value;
        if (ast instanceof ng.Interpolation) {
          for (const text of ast.strings ?? []) {
            if (WORDS.test(text)) add(file, node.sourceSpan.start.line + 1, 'text', text);
          }
        }
        return expression(node.value, []);
      }
      if (node instanceof ng.TmplAstBoundAttribute) {
        if (MACHINE_BINDINGS.test(node.name)) return;
        // `title="Due {{ when }}"`: the same string list, on an attribute a person reads.
        const ast = node.value?.ast ?? node.value;
        if (ast instanceof ng.Interpolation && COPY_ATTRS.has(node.name.replace(/^attr\./, ''))) {
          for (const text of ast.strings ?? []) {
            if (WORDS.test(text)) add(file, node.sourceSpan.start.line + 1, 'attr', `${node.name}="${text}"`);
          }
        }
        expression(node.value, []);
        return;
      }
      if (node instanceof ng.TmplAstBoundEvent) return expression(node.handler, []);
      if (ng.TmplAstSwitchBlockCase && node instanceof ng.TmplAstSwitchBlockCase) {
        (node.children ?? []).forEach(template);
        return;
      }
      if (node instanceof ng.AST || node instanceof ng.ASTWithSource) return expression(node, []);
      for (const [key, value] of Object.entries(node)) {
        if (SKIP_KEYS.has(key)) continue;
        if (Array.isArray(value)) value.forEach(template);
        else if (value && typeof value === 'object') template(value);
      }
    };
    parsed.nodes.forEach(template);
  };

  for (const file of files) {
    const source = fs.readFileSync(file, 'utf8');
    try {
      if (file.endsWith('.html')) scanHtml(file, source);
      else scanTs(file, source);
    } catch (error) {
      add(file, 0, 'scanner-error', error.message);
    }
  }

  const allowed = (hit) =>
    ALLOW.some(
      (entry) => hit.file.endsWith(entry.file) && (entry.texts ?? [entry.text]).includes(hit.text),
    );
  const open = hits.filter((hit) => !allowed(hit));

  if (process.argv[2] === '--detail') {
    const fragment = process.argv[3];
    let current = '';
    for (const hit of hits.sort((a, b) => a.file.localeCompare(b.file) || a.line - b.line)) {
      if (fragment && !hit.file.includes(fragment)) continue;
      if (hit.file !== current) console.log(`\n### ${(current = hit.file)}`);
      console.log(
        `  ${String(hit.line).padStart(4)}  ${hit.kind.padEnd(8)} ${allowed(hit) ? '(allowed) ' : ''}${hit.text.slice(0, 120)}`,
      );
    }
  } else {
    const byFile = new Map();
    for (const hit of open) byFile.set(hit.file, (byFile.get(hit.file) ?? 0) + 1);
    for (const [file, count] of [...byFile].sort((a, b) => b[1] - a[1])) {
      console.log(String(count).padStart(4), file);
    }
  }
  console.log(
    `\n${open.length} to fix in ${new Set(open.map((hit) => hit.file)).size} files (${hits.length - open.length} allowlisted)`,
  );
  process.exitCode = open.length > 0 ? 1 : 0;
}

main().catch((error) => {
  console.error(error);
  process.exit(2);
});
