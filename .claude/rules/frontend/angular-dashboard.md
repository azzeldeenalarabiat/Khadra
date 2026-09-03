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
- Every money value shows its currency code (JOD), either on the value or in the panel header.
- Restricted actions stay visible but disabled with a tooltip explaining why.

## Before reporting completion

- No `any` unless an external library forces it.
- `npm run build -- --configuration production` passes.
