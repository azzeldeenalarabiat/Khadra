---
paths:
  - "Khadra.Dashboard/src/**/*.ts"
  - "Khadra.Dashboard/src/**/*.html"
  - "Khadra.Dashboard/src/**/*.scss"
  - "Khadra.Dashboard/package.json"
  - "Khadra.Dashboard/angular.json"
---

# Angular Dashboard Rules

## Source of truth for the look

`docs/design/` holds the exported Claude Design project. Those `.dc.html` files decide how the
console looks. Before changing a screen's appearance, diff against them; if the design changed,
re-export first rather than adjusting the Angular by eye.

## Components

- Angular 22 standalone components, `ChangeDetectionStrategy.OnPush`, Signals for local state.
- New components contain only `.component.ts` and `.component.html`. No `.component.scss/.css`, no
  per-component `.spec.ts`, no inline `styles`, no `styleUrls`, and no `style="…"` attributes in
  templates. Every rule lives in `src/styles/`.
- Keep API URLs out of components (use `core/services`). Keep business logic out of templates.

## Styling

- `src/styles/_tokens.scss` and `_ds-components.scss` are ported from the design system's
  `ds/styles.css`. Change them only to track a design-system change.
- Status colour is carried by a `--c` custom property set by `.s-ok` / `.s-warn` / `.s-bad` /
  `.s-dim` / `.s-accent`. Never write a literal status colour in a template or a new rule.
- Use logical properties (`margin-inline-start`, `inset-inline-end`, `text-align: end`) so the
  console flips correctly under `dir="rtl"`. `box-shadow` has no logical form; mirror it explicitly.

## Component library

**This console does not use PrimeNG.** The rule elsewhere in this project says to prefer PrimeNG
community components, and the Admin console is a deliberate, recorded exception: it is built to a
bespoke design system (Nocturne) with its own tokens, and theming PrimeNG to match would have cost
fidelity for no gain. The console's own shared components (`kh-data-table`, `kh-timeline`,
`kh-doc-tile`, `kh-confirm-modal`, `kh-toast`, `kh-icon`) fill that role.

If a future screen needs something heavy that PrimeNG already solves well — a date-range picker, a
virtualised tree — bring PrimeNG in for that widget and theme it with the tokens, rather than
reverting the whole console.

## Behaviour

- All server calls go through the BFF (`/bff/*` for session, `/api/*` proxied). The browser never
  stores tokens.
- Handle ASP.NET ProblemDetails: show field errors from `errors`, show `traceId` for support.
- Destructive admin actions go through `kh-confirm-modal`, which states the consequence before it
  happens. Confirmations report back as a toast.

## Content

- **English and Arabic, LTR and RTL, switched at runtime.** `core/i18n/` holds it: `I18nService`
  exposes `t(key, params)`, `FormatService` every number and date. Both read a signal, so a switch
  re-renders everything -- including copy built inside `computed()`s, which most of this console is.
  - **No user-facing string literal in a component or a template.** Add the key to `en.ts` (which
    defines the key type) and to `ar.ts` (typed against it, so a missing key fails the build).
  - **Hold KEYS in state, never resolved words.** A banner set at submit time and stored as a
    sentence stays in the language it was written in when someone switches; store the key.
  - Presenters and other pure functions take `t` as a parameter rather than injecting it.
  - Never `toLocaleString('en-GB')`; go through `FormatService`. Digits are Latin under `ar-JO`
    (`-u-nu-latn`) on purpose -- Jordanian commercial software uses them, and the server's own
    references and plates are Latin.
  - Arabic plurals need all six categories. `Intl.PluralRules` picks; the dictionary supplies.
  - Wrap Latin runs inside Arabic text -- emails, `+962` numbers, plates, references, signed amounts
    -- in `.ltr`, and text somebody typed in `.user-text`, or bidi reorders them.
  - `letter-spacing` breaks Arabic's cursive joins; `_rtl.scss` zeroes it under `:lang(ar)`.
  - Mirror only direction-carrying icons, with `class="icon-mirrored"`. Not clocks, cars or charts.
  - **Status names go through `I18nService.statusLabel(name, scope)`**, never a local map to English
    words. Where a reader words a status differently (the dealer's queue calls `Requested` "Pending"),
    that wording is a scoped dictionary entry (`status.requestedDealerBooking`), resolved most specific
    first. A flag that is not a status (a live dispute) is worded with its own key.
  - **Refusals are held as facts.** A failed request goes into a `ProblemSnapshot`
    (`core/i18n/problem.ts`) and its words are chosen in a `computed`, so switching language re-words
    a refusal already on screen. The server writes ProblemDetails and validation messages in English:
    show them only through `serverSentence` / `fieldMessage`, which use them in English and a localized
    line in Arabic — never `error.title` or `errors[field][0]` directly.
  - **Time is worded by `FormatService`:** `relative` ("3 hr ago", "in 2 days"), `dayAndTime`
    ("tomorrow 09:00"), `duration` ("13h"). A server `YYYY-MM-DD` is a calendar date, not an instant:
    `calendarDayMonth`, never `new Date(iso + 'T00:00:00')`.
  - **A clock against a deadline is one of two things, and they read differently** (owner, 2026-09-17):
    a chance that runs out — a request to answer, a deposit to pay, a signed link — is
    `formats.deadline(iso, closed)`: "2h remaining", then a terminal "Expired"; a promise that can be
    broken — a dispute SLA, an application review — is `formats.sla(iso, breached)`: "2h remaining",
    then "Overdue by 13h". Both floor the figure and never show less than a minute, and both take the
    SERVER's flag as well as the clock, so a browser whose time is behind cannot keep a dead deadline
    alive. Never hand-write "13h over" or "7h left".
  - **Enums that are not statuses** — a booking party, a handover type, a penalty reason — go through
    `I18nService.enumLabel(family, name)`, with the same spelled-out fallback as `statusLabel`.
  - A penalty's reason is worded from its `reasonCode`; a booking assessed before codes existed shows
    its frozen English sentence in a `.ltr` run, never a guess made from the text.
  - **Arabic vocabulary matches the Customer App** (owner, 2026-09-13): a dealership is "مكتب" /
    "مكتب التأجير", a vehicle "سيارة", the brand "خضرا". `dictionaries.spec.ts` fails on "معرض" or
    "مركبة".
- Every money value shows its currency code, taken from the value's own `currency` — never a literal
  `'JOD'` default. The platform's own figures carry theirs; a hard-coded code is a claim about a
  number that came from somewhere else.
- **A template never types a sign in front of an amount.** A literal "−" before `{{ x | money }}`
  printed "−0" on a zero commission, and in Arabic, outside the formatter's bidi isolate, "JOD 0−".
  The platform commission is shown as the positive amount Khadra charges (owner, 2026-09-13); the
  labels carry the subtraction, and the server computes the net. `FormatService` also refuses to print
  negative zero.
- Restricted actions depend on WHY they are restricted, and the two are not the same restriction:
  - **A state you are waiting out** — a dealership not yet approved, a suspension, a booking in the
    wrong status — keeps its control, disabled, with a `title` naming what unlocks it. The action is
    yours; it is not available *yet*.
  - **A role that is not yours** — the fleet for an employee, staff management, the dealer page —
    drops the controls entirely and says once, in a `banner s-warn` at the top of the screen, whose
    they are. Four dead buttons per card is noise, and half of them cannot be disabled anyway: an
    `<a routerLink>` ignores `[disabled]` and stays clickable.
- **Never decide a permission from a failed request.** A role failure returns a *bodiless* 403 —
  `ProblemDetailsAuthorizationResultHandler` writes ProblemDetails only when the approved-dealer
  handler is the one that failed — and every screen renders a bodiless failure as "the service did
  not respond", which reads as a broken platform to someone who is simply not the owner. Ask
  `GET /dealers/me` instead and decide before the click: `DealerConsoleService.permissions` holds one
  field per API policy. It is three-valued — `null` until that call answers — and every consumer
  branches on `null` separately, or an owner's own controls flash in a beat late and a guard bounces
  them off their own form on a cold load.
- **A resource you gate must not strand its screen.** Once a request is not sent, no 403 ever
  arrives, so a screen that derives "you may not see this" from `error()` waits on a skeleton
  forever. Render the denied state from the same permission that suppressed the request, and keep
  the 403 branch for the grant that is revoked while the screen is open.

### No business number is written into a screen

The backend rule (business numbers come only from `IBusinessRulesProvider`) has a counterpart here:
**a number an administrator could act on is never a literal in a component or a template.** Not the
review SLA, not how many documents an approval needs, not how long a signed link lasts, not the
threshold at which something counts as "at risk". Each of these was in the console and each was
right only by coincidence — "the 48-hour review SLA", "3 of 3 documents", "expire in 5 minutes",
`hours < 12` — and each would have gone on being printed unchanged after the configuration behind it
moved.

Two ways to get the number honestly, in this order:

1. **Derive it from the record**, when the record froze it. An application's promised window is
   `reviewDueAt - submittedAt`, because `Dealer.Register` freezes `reviewDueAt` at submission
   precisely so a later settings change cannot re-judge it. Reading the *current* setting here would
   be a different lie, not a fix: the sentence would contradict the countdown beside it.
2. **Ask the API**, when it is current policy rather than a frozen fact — `requiredDocuments`,
   `requiredDocumentCount`, `adminSlaHours` on the dashboard snapshot.

The same goes for anything invented per record: a filename, an extension, a currency, a rating. If
the server does not send it, the screen does not know it. Show what is true (the document's format)
or show nothing — never a plausible-looking placeholder, which is indistinguishable from real data
to the person acting on it.

## Before reporting completion

- No `any` unless an external library forces it.
- `npm run build -- --configuration production` passes.
- `npm run i18n:check` passes. It parses every template and component and fails on user-facing copy
  outside the translation system, on `toLocale*`/`Intl` outside `core/i18n`, and on `toFixed` in a
  component. A string that is genuinely correct in Latin script on an Arabic screen goes in its
  `ALLOW` list with the reason — never an exclusion by file.
