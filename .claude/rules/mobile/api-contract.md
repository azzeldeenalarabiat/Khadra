---
paths:
  - "Khadra.Mobile/lib/api/**/*.dart"
  - "Khadra.Mobile/lib/core/api/**/*.dart"
  - "Khadra.Mobile/lib/core/config/**/*.dart"
  - "Khadra.Mobile/pubspec.yaml"
---

# The customer app's contract with the API

The code here decides what the app reads from the API and how it introduces itself. Builds already
installed on phones go on reading whatever they were built to read, and none of them can be patched,
only refused. The rule in CLAUDE.md, "The customer app's contract", applies to every change:

- **A breaking contract change raises the minimum.** When the API stops serving something an
  installed build reads — a field removed or renamed, a type or shape changed, a code renamed, an
  endpoint moved — the same change set raises `version:` in `pubspec.yaml` to a new
  MAJOR.MINOR.PATCH and `MobileApp:MinimumSupportedVersion` in `Khadra.WebAPI/appsettings.json` to
  that version. The `+build` number alone never counts: the comparison ignores it.
- **The new build is published before the raised minimum reaches production.** A minimum raised
  first refuses every customer with nothing to update to.
- **So the new build must still run against the API that is live.** A field it cannot read degrades
  to "nothing to show", never to a thrown cast — `ResolvedText.maybe` and `MobileAppConfig.fromJson`
  are the pattern. That tolerance is what makes publishing first possible.
- **Never stop sending `X-Khadra-App-Version`** (`AppVersionInterceptor`), and never change its
  format. The API identifies every build from 1.1.0 by it; a build that sends the app's User-Agent
  without it is taken to be older than any minimum, and refused.
- **A `426 app.update_required` is never a verdict on the customer.** It must not end the session and
  must not read as bad credentials.

The wire contract and the release order are in `docs/contracts/README.md`.
