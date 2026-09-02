---
paths:
  - "Khadra.Dashboard/src/**/*.ts"
  - "Khadra.Dashboard/src/**/*.html"
  - "Khadra.Dashboard/src/**/*.scss"
  - "Khadra.Dashboard/package.json"
  - "Khadra.Dashboard/angular.json"
---

# Angular Dashboard Rules

- Angular 22 standalone components, `ChangeDetectionStrategy.OnPush`, Signals for local state.
- New components contain only `.component.ts` and `.component.html`. No `.component.scss/.css`, no per-component `.spec.ts`, no inline `styles`, no `styleUrls`. Use global SCSS and theme tokens.
- Use PrimeNG community components; match the PrimeNG major to the Angular major. No paid templates.
- Keep API URLs out of components (use `core/services`). Keep business logic out of templates.
- All server calls go through the BFF (`/bff/*` for session, `/api/*` proxied). The browser never stores tokens.
- Handle ASP.NET ProblemDetails: show field errors from `errors`, show `traceId` for support.
- Support English and Arabic, LTR and RTL, from day one. Every money value shows its currency code (JOD).
- Restricted actions stay visible but disabled with a tooltip explaining why.
- No `any` unless an external library forces it. Run `npm run build -- --configuration production` before reporting completion.
