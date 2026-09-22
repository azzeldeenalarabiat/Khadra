---
name: review-pr
description: Review a backend or dashboard diff against CLAUDE.md, the DDD/CQRS/REST rules, Result-type error handling, test coverage, and migration safety.
argument-hint: <number-or-branch>
---

Review the diff for `$ARGUMENTS` (PR via `gh pr diff`, branch via `git diff main...<branch>`, default current branch vs `main`).

Check against `CLAUDE.md` and `.claude/rules/**`:
- Domain: public setters, plain `enum` instead of smart enum, primitives where a value object exists, missing `ISoftDeletable`, invariants enforced outside the aggregate, business numbers as constants.
- Application: exceptions thrown for expected failures instead of `Result`, handlers touching `HttpContext`, missing validator, missing `SaveChangesAsync`, cross-context aggregate mutation.
- Infrastructure: missing `DeleteBehavior.Restrict`, cross-context navigation properties, secrets in tracked config, hand-edited existing migration (always Critical).
- API/BFF: wrong status codes, missing rate limit on anonymous endpoints, tokens exposed to the browser, missing antiforgery on BFF mutations.
- Customer-app contract (always Critical): a breaking change to anything `Khadra.Mobile` reads or sends — a field removed or renamed, a type or shape changed, a code or enum value renamed, an endpoint removed or moved, a request newly refused — without `MobileApp:MinimumSupportedVersion` in `Khadra.WebAPI/appsettings.json` raised to a new MAJOR.MINOR.PATCH in `Khadra.Mobile/pubspec.yaml` in the same diff. Say in the finding that the new build must be published before the API carrying the minimum is deployed.
- Tests: new aggregate behavior or handler without tests.
- Frontend: violations of `.claude/rules/frontend/angular-dashboard.md`.

Output findings grouped as **Critical** / **Important** / **Minor** with file:line, the rule violated, and a one-line fix.
