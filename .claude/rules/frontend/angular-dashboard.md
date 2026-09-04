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

- Support English and Arabic, LTR and RTL. The CSS is direction-agnostic; Arabic copy and a language
  switcher are still outstanding.
- Every money value shows its currency code, taken from the value's own `currency` — never a literal
  `'JOD'` default. The platform's own figures carry theirs; a hard-coded code is a claim about a
  number that came from somewhere else.
- Restricted actions stay visible but disabled with a tooltip explaining why.

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
