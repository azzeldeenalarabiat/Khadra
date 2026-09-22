# The customer app's contract with the API

What a customer-app build already on a phone depends on, how the API turns away a build too old
for what it now serves, and the order in which a breaking change has to reach phones.

The rule is in [CLAUDE.md](../../CLAUDE.md), "The customer app's contract", settled by the owner on
2026-09-22:

> Any breaking mobile/API contract change ships with a corresponding raise of the minimum supported
> app version, and the compatible mobile build is published before the server minimum is raised.

This page is the reference behind it.

---

## Why a rule, and not care

An installed build cannot be patched. It goes on reading exactly what it was built to read until its
owner installs another, and the API cannot tell it to without a mechanism both sides agreed on in
advance. The first breaking change — the office texts becoming `{ "text", "language" }` where they
had been strings (pre-launch items 132 and 133) — found that 1.0.0 had no such mechanism: it would
have crashed on every car page and most gallery pages, the pages where booking starts. 1.1.0 is the
first build that takes part.

## What counts as breaking

Anything an installed build would misread or fail on:

- a field it reads removed or renamed;
- a field's type or shape changed — a string becoming an object was the first;
- a code or enum value it keys on renamed, or given a new meaning;
- an endpoint it calls removed, moved, or answering with a different status for the same case;
- a request it sends refused where it used to be accepted — a field newly required, a format newly
  rejected.

Not breaking: an added optional field, a new endpoint, a new code the app does not know (it falls
back to the server's own sentence). **Prefer the additive shape** — the old field kept beside the new
one until no build reads it — whenever it is honest. When it is not, the rule applies in full.

## How a build introduces itself

| | Sent by | Value |
|---|---|---|
| `X-Khadra-App-Version` | every build from **1.1.0**, on every request to the API's own origin — never to another host | `<versionName>+<versionCode>` from `package_info_plus`, e.g. `1.1.0+2` |
| `User-Agent: Khadra (<platform>)` | every build since d6fddef (2026-09-21), e.g. `Khadra (Android 16)` | platform identification |

A request carrying the app's User-Agent **without** the version header is a build from before 1.1.0,
and the API treats it as older than any minimum. Builds from before 2026-09-21 send Dart's default
User-Agent, which any Dart program sends; nothing tells them apart from other traffic, so they cannot
be refused. A web build cannot set its User-Agent at all, so an old web build is never identified; a
new one sends the header.

## What the API publishes

`GET /api/v1/app-config`, open to every build:

```json
"mobileApp": { "minimumSupportedVersion": "1.1.0", "updateUrl": null }
```

| Setting | Value |
|---|---|
| `MobileApp:MinimumSupportedVersion` | A release version (`1.1.0`, never `1.1.0-rc.1`), or empty for no minimum. Shipped in `Khadra.WebAPI/appsettings.json` |
| `MobileApp:UpdateUrl` | An absolute `http(s)` address where the current build can be downloaded, or empty. Set per environment |

An invalid value stops the API at startup. Every boot logs which builds it refuses, category
`Khadra.MobileApp`: `Customer app: builds older than 1.1.0 are REFUSED with 426 app.update_required…`
or `Customer app: no minimum version is set, so every build is served.`

## The refusal

`MobileAppVersionGate` runs after CORS and the rate limiter and **before authentication**, so a refused
build never reaches a token check, a session or a handler. A request is refused when it is the app and
its version is below the minimum, or its header is empty, repeated or unreadable, or it carries the
app's User-Agent without the header:

```http
HTTP/1.1 426 Upgrade Required
Content-Type: application/problem+json
Cache-Control: no-store

{"type":"https://httpstatuses.com/426",
 "title":"حدّث تطبيق خضرا للمتابعة · Update the Khadra app to continue",
 "status":426,
 "detail":"هذا الإصدار من التطبيق لم يعد مدعومًا. ثبّت أحدث إصدار من خضرا. · This version of the app is no longer supported. Install the latest version of Khadra.",
 "instance":"/api/v1/vehicles",
 "code":"app.update_required",
 "traceId":"…",
 "minimumSupportedVersion":"1.1.0",
 "updateUrl":null}
```

The title and detail carry both languages when the request names no `Accept-Language` — which is how
1.0.0 calls, and it prints the title verbatim — and the request's language otherwise. There is no
`Upgrade` header: HTTP/2 forbids it. **Clients key on `code`, never on the status alone.** From 1.1.0
the app answers it with a full-screen update screen that replaces the router, keeps the stored
session, and never reads it as bad credentials.

**Never gated:** anything when no minimum is set; every path outside `/api` (`/health/live`,
`/health/ready`, `/sandbox-checkout/…`, the development OpenAPI and Scalar pages);
`/api/v1/app-config`; and every request that is not the app — the console through the BFF, browsers,
server-to-server calls, CORS preflights.

## Version order

[Semantic Versioning 2.0.0](https://semver.org/#spec-item-11) precedence, never text comparison:
numbers as numbers (`1.10.0` > `1.9.0`), a prerelease below its release (`1.1.0-rc.1` < `1.1.0`), build
metadata ignored (`1.1.0+2` = `1.1.0`). A build passes when its version is at or above the minimum.

The cases live in [app-version-vectors.json](app-version-vectors.json), and **both** suites run that one
file — `AppVersionTests` in C# and `app_version_test.dart` — so the server and the phone cannot come to
disagree about which build is newer. An unreadable version is refused by the server; an unreadable
minimum is ignored by the app, which then defers to the server.

## Changing the contract

### In the repository — one change set

1. **The app reads the new contract, and still runs against the old one.** A field it cannot read
   degrades to "nothing to show" rather than throwing, so the build can be published before the API
   changes. `ResolvedText.maybe` is the pattern.
2. **`Khadra.Mobile/pubspec.yaml` rises to a new MAJOR.MINOR.PATCH.** The `+build` number alone does
   not count: the comparison ignores it.
3. **The API serves the new contract.**
4. **`MobileApp:MinimumSupportedVersion` in `Khadra.WebAPI/appsettings.json` rises to that version**,
   with a test saying why the previous build must stay refused —
   `The_minimum_this_release_ships_with_keeps_the_installed_1_0_0_build_out` is the example.
   `MobileAppMinimumVersionTests` fails if a tracked settings file ever sets the minimum above the
   app's own version.

### In production — the order is fixed

1. **Build and sign the new app** — with the same key as the builds on phones, or it will not
   install over them — and **publish it** where `MobileApp:UpdateUrl` points.
2. **Confirm it works against the API that is live.**
3. **Only then deploy the API carrying the raised minimum**, with whatever migration it needs
   ([production.md](../production.md)). Read the `Customer app: builds older than …` boot line.
4. **Confirm the new build is served, not refused.** It must load normally, and the API log must show
   no `Refused an old customer-app build (…)` line for it. If it does, clear the minimum
   (`MobileApp__MinimumSupportedVersion` set empty) and restart while you find out why.

A minimum raised before step 1 refuses every customer with nothing to update to. That order has no
test: it is the rule.
