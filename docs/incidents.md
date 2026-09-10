# Incidents

Bugs that reached production or came close, what actually caused them, and what
changed so they cannot recur quietly.

They are here because the root cause is almost never the thing that looked broken,
and because several share one shape: **a fallback that did something reasonable
without saying so.**

---

## The console loaded and nobody could sign in

**Symptom.** `GET /bff/antiforgery` — the first call the sign-in page makes —
answered 500 with `AntiforgeryOptions.Cookie.SecurePolicy = Always, but the current
request is not an SSL request`. The console itself rendered perfectly, which is what
made it read as an antiforgery bug.

**Cause.** Neither. Render's router connects to the container over **loopback**, so
the transport peer is `::1`. An earlier fix had deliberately cleared the framework's
default trust of loopback — correct in principle — which made `::1` an untrusted
sender. `X-Forwarded-Proto: https` was therefore discarded, `Request.IsHttps` stayed
false, and antiforgery refused to issue a `__Host-` cookie over what it believed was
plain HTTP.

**What made it hard.** Two rounds of diagnostics. The first logged the *first*
request and spent its one shot on a platform health probe — loopback, no forwarding
headers at all — arriving a minute before the browser request that failed. Read
literally, that line argued for trusting a health check.

**The trap in the fix.** Trusting `::1` alone makes sign-in work, and it is tempting
to stop there. It resolves every visitor to Render's load balancer. Since the
sign-in cap is keyed on `{address}|{account}`, a constant address turns it into a
**per-account bucket shared with the attacker** — ten requests a quarter hour aimed
at a named administrator holds that account shut, and the victim cannot move out of
the way.

`ForwardLimit` does not rescue it: it is a **ceiling, not a target**. Trust is
re-checked at each hop against the address just consumed, so the walk stops at the
first untrusted address however high it is set. Measured on the real chain with only
loopback trusted, limits of 1, 2 and 3 resolved the identical address — which means
the deployment guide's "raise it by one and repeat" could never have worked.

**Fixed by** trusting the chain that actually exists — loopback, `10.0.0.0/8`, and
Cloudflare's published ranges — at `ForwardLimit=3` exactly. Not "trust everything":
the only way to exploit it is to *connect* from a trusted range, and Cloudflare
appends rather than replaces, so anything a caller invents lands to the left of
their true address. Exactly 3 because a visitor whose own address is in a trusted
range (a Cloudflare Worker) plus one hop too many hands them the partition —
measured: 3 resolves the Worker, 4 resolves the junk.

**Pinned by** `ForwardedHeaderChainTests`.

---

## Every email was "sent" successfully to nobody

**Symptom.** `POST /api/v1/auth/forgot-password` answered `202` and logged
`Handled ForgotPasswordCommand in 975 ms`, and no request to Brevo followed.
Registration, administrator invitation and password reset were all in the same
state: reported as sent, delivered to nobody.

**Cause.** The mail transport is chosen by matching a string, and the branch that
caught everything unrecognised registered `LoggingEmailSender` — **silently**. That
sender accepts every message, writes it to the log, and returns success, so the
dispatcher reported a successful send and the screen told people a link was on its
way.

**Three things kept it hidden**, and the third is probably how it happened:

1. The startup probe reported **ready**, so the boot log said
   `Email ready. Email:Provider is 'Logging'. Messages are written to the log and
   delivered to nobody.` — a sentence at war with itself, whose first two words are
   the only part anyone scanning a log reads.
2. `Logging` is the **default**, so forgetting the variable selects it.
3. `docs/deployment.md` gave the value in **shell quoting**: `Email__Provider="Brevo"`.
   A shell strips those quotes; a dashboard field keeps them, and `"Brevo"` matches
   no transport. Following the documentation exactly produced the fault.

**Fixed by** refusing an unrecognised value at startup — quoting it in the message
so a stray quote or trailing space is *visible* — refusing `Logging` in Production
specifically, making the probe report NOT ready and name the value it read, and
rewriting the documentation to give bare values with the shell/dashboard distinction
stated.

**A consequence worth knowing.** While that transport was selected, every message —
**including its link** — went into the application log, and those links are working
credentials until their token expires: 60 minutes for a reset, 24 hours for
verification, **7 days for an invitation**. A logged administrator invitation is a
seven-day credential to become the platform administrator. The remedy is in
[sql/verify-admin.sql](sql/verify-admin.sql).

**Also fixed here.** The reason a reset produced nothing was unknowable from
outside, by design — the response is deliberately uniform. It now logs one line per
outcome (events `1300`–`1304`), keyed by correlation id, never containing the
address, the token or the link.

---

## The container could not create the folder it stores documents in

**Symptom.** Every gallery application answered `500 An unexpected error occurred`
at the moment it tried to save the first licence scan.

**Cause.** `Documents:RootPath` is `App_Data/documents` — **relative**, so it
resolves under the image's working directory. That is `/app`, which the base image
leaves `root:root 755`, and the server runs as the unprivileged `app` user. So
`Directory.CreateDirectory("/app/App_Data/documents/dealers/<id>")` was refused and
the request died with `UnauthorizedAccessException`.

**Why nothing caught it earlier.** Nothing had ever reached it. Writing a file needs
a dealer owner who has registered, verified their address, signed in and filled in
the entire application form — and the first person to do all that in production was
the first person to attempt a write at all.

**Why the end-to-end tests missed it.** They drove `dotnet run` **on the host**,
where the directory is writable, instead of the image that actually ships. Proved by
running both images side by side against one database with the same request: the
production image answered 500, the fixed one answered 201.

**Fixed by** creating and `chown`ing that one directory in the Dockerfile before
dropping to the `app` user — everything else under `/app` stays root-owned and
read-only to the server.

**But that was not the real fix.** The directory lives and dies with the container,
so on an ephemeral filesystem every licence scan still disappeared on the next
deploy. Documents subsequently moved to a **private Supabase bucket**, and Production
now refuses to start on the local store.

---

## The bootstrap that declined without saying so

**Symptom.** Setting `Admin__Bootstrap__Email` and restarting did nothing, with
nothing in the log mentioning the subject at all — so the only conclusion available
was that the setting had not been read.

**Causes**, two of them:

- The address may already belong to somebody — likeliest on exactly the deployment
  that matters, where the owner had registered through the customer app with the
  address they later configured. The insert hit the unique index and landed in the
  race-loss catch, which reported *"created by another instance; this one did
  nothing"* — false, and it sends the reader hunting for an instance that does not
  exist. The **phone** collides identically, and is likelier still.
- The reissue path declined silently in four situations. The worst is a
  **soft-deleted administrator**: `AnyAdminExistsAsync` ignores the query filter
  while `GetByEmailAsync` honours it, so such a row both blocks the bootstrap and is
  invisible to the code meant to rescue it. The platform is locked out of its own
  console with nothing saying why.

**Fixed by** checking both the email and the phone before inserting — re-asking
whether an administrator exists before blaming the configuration, since a replica
that won the race is not a misconfiguration — and logging on every exit.

**A third thing found while tracing it.** Accepting an invitation set a password
unconditionally, so a link stayed a working credential for its whole seven days
**even after the owner had the account** — using it would overwrite their password.
Given invitations had been written into the log (above), that was live. Now refused
once a password exists.

**A correction worth recording.** It was initially reported that the invitation link
is the *only* route to a first login. That is wrong: `ResendVerificationHandler`
gates on the address and never on the role, so an invited administrator can verify
through `/verify-email` and then reset normally. Accidental, but sound — both links
go to the same mailbox.

---

## The map that only appeared once you already had the location

**Symptom.** A dealer owner could not select their city, and could not submit their
application at all.

**Cause.** The map only rendered once coordinates existed — so it was useful for
*adjusting* a location and useless for *choosing* one, which is the only thing a new
owner needs. Cities came back empty because none had been created, and the form gave
no way forward.

**Fixed by** rendering the map from the start, with the city's centre when it has
one and an explicit message when it does not; adding click-to-place, drag,
"use my current location", and manual coordinates; and proxying reverse geocoding
server-side so the pin suggests an area and street the owner can overwrite.

Cities remain **real data created by an administrator** — no seeder, because a
fabricated city is exactly the invented data the standing rule forbids.

---

## The patterns worth carrying forward

**A silent fallback is a bug even when it works.** Three separate incidents here
were a branch that did something reasonable without saying so: the mail transport,
the document store, and the bootstrap's early returns. The fix is the same each
time — refuse, or say why, and name the setting.

**"Ready" must mean ready.** A probe that reports healthy for a transport that
delivers nothing is worse than no probe, because it stops the investigation.

**Test the artefact you ship.** A host process is not a container image. Users,
permissions and paths differ, and that difference hid a 500 until a real person hit
it.

**One shot at a diagnostic is usually spent on the wrong request.** Health probes
arrive first. Target the request that actually fails.

**When a fix makes the symptom go away, ask what it resolved to.** Trusting `::1`
fixed the 500 and left a per-account lockout vector. The symptom disappearing is not
the same as the problem being solved.
