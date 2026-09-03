# Admin Console design source

Exported from the Claude Design project
`https://claude.ai/design/p/abfd4b04-c3e3-43e6-99c8-747bf8e2ebb0`.

These `.dc.html` files are the **source of truth for how the Admin console looks**.
When a screen is changed in Claude Design, re-export here and diff before changing
`Khadra.Dashboard`.

| File | What it defines |
|---|---|
| `Admin Console.dc.html` | All 22 screens, the modals and the toast |
| `AdminSidebar.dc.html` | Navigation rail and its groups |
| `AdminTopbar.dc.html` | Breadcrumb, search and account controls |
| `ds/styles.css` | Nocturne design tokens and component classes |
| `icons.js` | The line-icon set |

## How it maps onto the Angular app

- `ds/styles.css` → `src/styles/_tokens.scss` and `_ds-components.scss`, ported verbatim.
- Inline styles in the design → named classes in `_utilities.scss`, `_layout.scss`, `_screens.scss`.
  The design repeats style attributes because a canvas has no stylesheet; the app must not,
  per `.claude/rules/frontend/angular-dashboard.md`.
- `icons.js` → `src/app/shared/icon/icon-paths.ts`, generated from this file so the SVG paths match exactly.
- The design's `lists()` function → `src/app/core/data/lists.data.ts`, same shape.
- The design's `modals()` function → `src/app/core/data/modals.data.ts`.
- The design's `state` object → `ConsoleUiService` plus per-component signals.

The design's sample content lives in `src/app/core/data/`. Those constants are shaped like the
API responses the console will eventually call, so wiring the real API replaces the data files
rather than the components.
