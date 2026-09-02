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
- Tests: new aggregate behavior or handler without tests.
- Frontend: violations of `.claude/rules/frontend/angular-dashboard.md`.

Output findings grouped as **Critical** / **Important** / **Minor** with file:line, the rule violated, and a one-line fix.
