# Testing

How this system is tested, what the suites currently say, and the two failure modes
that will otherwise waste your time.

---

## Current results

Measured on `main` at the time of writing.

| Suite | Command | Result |
|---|---|---|
| Backend | `dotnet test Khadra.slnx` | **1132 tests** |
| Console | `npx ng test --watch=false` (in `Khadra.Dashboard`) | **63 tests**, 6 files |
| Mobile | `flutter test` (in `Khadra.Mobile`) | **53 tests** |

> **Two backend tests need PostgreSQL.** `ForwardedHeaderTrustTests` and
> `MailTransportConfigurationTests` boot a real host that touches the database. With
> Docker stopped you will see exactly two failures, both
> `Npgsql: Failed to connect to 127.0.0.1:5432`. Run `docker compose up -d` and they
> go green. If you see *those two* failing and nothing else, it is your environment,
> not your change.

---

## Where the backend tests live

```
Khadra.Tests/
  Domain/         pure aggregate behaviour, no doubles
  Application/    handlers with NSubstitute doubles
  Persistence/    SQLite in-memory round-trips
  Security/       WebApplicationFactory against a real pipeline
  Infrastructure/ adapters, with a fake HttpMessageHandler
```

Each layer answers a different question, and the split is deliberate.

### Domain — is the rule right?

No doubles at all. Aggregates are exercised directly: pricing, day counts, the
booking state machine, `CanAuthenticate`, holds and turnaround.

### Application — does the use case orchestrate correctly?

NSubstitute doubles for repositories and ports. `AuthHandlerTestContext` builds the
whole collaborator set once so a test overrides only what it asserts on.

### Persistence — did EF actually store it?

SQLite in-memory, and these earn their keep. **Every property of a value object
mapped with `ToJson()` must be asserted on a round trip**, because EF includes a
property by convention only when it has a setter, and ours are get-only by design —
so anything left to convention is silently dropped from the JSON with no error. That
is how several frozen booking fields were lost once.

### Security — does the real pipeline behave?

`WebApplicationFactory`, boots the actual `Program.cs`. This is where the guards are
pinned: forwarded-header trust, the mail transport and document-store configuration,
authorization policies, and every "refuses to start" rule.

Note the pattern these use: settings appended with `ConfigureAppConfiguration`
rather than `UseSetting`, because `Program.cs` re-adds `appsettings.Local.json`
*after* the host's own settings. A test that used `UseSetting` would pass on a clean
machine and fail on a developer's — which happened.

### Infrastructure — does the adapter speak the protocol?

A fake `HttpMessageHandler` asserts the exact request a provider builds: method,
path, headers, body — and what it does with each answer. The Supabase storage
provider's failure modes are pinned this way: a missing object versus a missing
bucket, a refused credential, a timeout, a duplicate write.

---

## What is asserted that you might not expect

**Log lines are tested.** `RecordingLogger<T>` captures what was logged, and the
password-reset tests assert both halves: that the reason *is* logged, and that the
address, the raw token and the link are *not*. A diagnostic that named the account
it declined to find would undo the very protection the silent response provides.

**Refusals are tested as carefully as successes.** Every "refuses to start" rule has
a test, and each asserts the message *names the offending setting* — because
"storage is misconfigured" sends somebody hunting through four variables.

**Dictionaries must match.** A console test asserts the English and Arabic
dictionaries have the same keys, so a screen cannot ship with an English string and
no Arabic one.

**The exact-limit edge case.** `ForwardedHeaderChainTests` pins that
`ForwardLimit=3` resolves a Cloudflare-Worker visitor correctly while `4` hands the
attacker the partition — a one-line configuration difference with a security
consequence, measured rather than reasoned about.

---

## End-to-end verification

Some things cannot be proven by a unit test, and the standing rule is to **drive the
real screens in a browser before reporting anything as done**.

What that has meant in practice:

- **A local edge that mimics production.** nginx terminating TLS and proxying over
  *loopback*, reproducing Render's chain exactly — including the `::1` peer that
  caused a production incident. Testing against `dotnet run` on the host would not
  have reproduced it.
- **A stand-in for Supabase Storage.** A small server speaking the same REST
  contract, storing objects *outside* the API, so persistence across a restart is a
  real observation rather than an inference. It enforces credentials, so "the key is
  actually sent" is proven too.
- **Side-by-side images.** When diagnosing the container permission bug, the image
  running in production and the fixed one were run against one database with the
  same request: 500 versus 201. That is the difference between a diagnosis and a
  guess.

The lesson from that last one is worth keeping: an end-to-end run against
`dotnet run` on the host missed a bug that only existed **in the container image**,
because the host directory was writable and `/app` is not. If a change touches
anything the image does differently — users, permissions, paths — test the image.

---

## Running them

```bash
docker compose up -d                                    # two Security tests need this

dotnet test Khadra.slnx                                 # backend
cd Khadra.Dashboard && npx ng test --watch=false        # console
cd Khadra.Mobile   && flutter test                      # mobile
```

To find why something failed rather than just that it did:

```bash
dotnet test Khadra.Tests/Khadra.Tests.csproj --logger "trx;LogFileName=r.trx"
# then read Khadra.Tests/TestResults/r.trx — the console output is easy to miss
```

---

## Writing new tests

- **Every use case ships with tests.** Domain behaviour and handler orchestration,
  at minimum.
- **Every business rule ships with a unit test.** This is a house rule, not a
  preference.
- **Name the test after the behaviour**, not the method:
  `A_second_write_at_a_committed_key_is_refused`, not `SaveAtAsync_Throws`.
- **Say why in the test, not just what.** The existing tests carry the incident that
  motivated them in their doc comments, which is what stops the next person
  "simplifying" a guard back out.
- **A test that depends on an external process does not belong in the suite.** The
  live storage round-trip was run and then deleted for exactly this reason.
