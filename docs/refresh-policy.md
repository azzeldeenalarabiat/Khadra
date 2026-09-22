# Refresh policy

One policy, written once, implemented twice. The Flutter customer app and the Angular console share
no code, so this document is the only thing that keeps them the same. Change it here first.

Settled by the owner on 2026-09-22.

## Why it exists

Two failures, opposite in shape, both found by measurement:

- A dealer sitting on the bookings queue **never saw a new request arrive**. The list is an
  `httpResource` keyed on tab and page; it fired once and never again. A browser reload was the only
  way — and the console has no refresh button on a healthy screen, only a *Retry* inside error blocks.
- A customer returning to the Bookings tab **re-read nothing**. The shell is an `indexedStack`, so the
  branch stays mounted and the `autoDispose` providers never dispose. (Pre-launch item 125.)

Both are "stale, and certain it is not", which is the only refresh failure that actually costs
anybody anything.

## Three tiers

Every remote read declares one. The tiers differ only in two numbers, which is deliberate — one
mechanism, three settings.

| tier | what | floor | poll |
|---|---|---|---|
| **Config** | `/app-config`, `/cities`, `/car-types`, `/vehicles/facets` | never re-read | no |
| **Live** | customer bookings list + counts · the landing card · dealer queue + counts · notifications feed + badge · booking detail while a deadline runs | 20s | yes |
| **On-demand** | dealer profile and permissions, delivery settings, fleet, reputation, `/dealers/me/dashboard` | 60s | no |

**Config** is immutable for a session. Re-reading it is pure waste and it is never polled.

**On-demand** is not "read once at startup", which is what the console did: an owner who changed the
delivery fee on another device saw the old one all day. It is re-read on entering the screen that
shows it, past a 60-second floor.

`/dealers/me/dashboard` is on-demand **because it is a composite** — revenue, 30-day occupancy,
upcoming handovers, recent activity. Polling all of that to learn one pending count is the request
storm this policy exists to avoid.

## Four triggers

| | trigger | floor applies | may show an error |
|---|---|---|---|
| **A** | became visible — tab selected, app resumed, route entered, browser tab shown | yes | no |
| **B** | poll, only while the surface is actually in front of the user | yes | no |
| **C** | after a write by this user | **no** | yes |
| **D** | push (the future pulse) — debounced ~1s | **no** | no |

**C is exempt from the floor.** A dealer who approves a booking five seconds after a poll must see the
row leave Pending. Flooring writes would break the one thing that already works.

**D is coalesced, not floored.** A push that waited 20 seconds would be worse than the poll it
replaces.

## Cadence

| surface | client | poll | notes |
|---|---|---|---|
| bookings list + counts | mobile | 60s | only while the Bookings tab is in front |
| the landing card (`nextBooking`) | mobile | 60s | only while Home is in front — the customer has two hours to pay |
| unread badge | mobile | 60s | the bar is on every tab, so it polls on every tab |
| notifications feed | mobile | — | trigger A only |
| booking detail | mobile | existing 30s tick | only while that booking's own deadline is live |
| dealer queue + counts | console | 30s | only on `/dealer/bookings`, which draws all eight counts |
| dealer pulse | console | 30s | two indexed counts; drives the reloads above |
| notifications feed | console | 60s | |

Mobile is slower because a phone on a Jordanian mobile network pays for both the battery and the
bytes. The console is a desk.

## Quiet and loud

**A refresh the user did not ask for may never take data away.**

- **Loud** — pull-to-refresh, *Retry*, after a write. May replace the screen with an error, because
  somebody is waiting for an answer and deserves to be told.
- **Quiet** — poll, became-visible, push. On failure it keeps the last good data, records when it was
  last good, and backs off. After two consecutive failures the surface says *"not updated since
  HH:MM"* rather than blanking.

Without this rule a 60-second poll on a flaky connection empties a dealer's queue and restores it a
minute later, which is worse than never polling at all.

## Stopping

Polling stops completely when the surface is not in front of a person:

- **Mobile** — anything but `AppLifecycleState.resumed`. Not a battery nicety: every background
  request runs the auth interceptor, which rotates the refresh token when the access token is stale.
  A backgrounded app would rotate every few minutes for ever, and each rotation is a chance to hit
  pre-launch items 126 and 128 and sign the customer out.
- **Console** — `document.hidden`, **and** ten minutes without a `pointerdown`, `keydown` or `wheel`.
  The idle gate is not optional. The BFF stores its session ticket in Redis with a sliding
  expiration, and *every* authenticated request slides it. A 30-second poll would keep an unattended,
  visible console alive to the 8-hour cap instead of dying at the 30-minute idle timeout — a security
  property changed by accident. The first input fires trigger A through the floor.

## Guards

- One in-flight reload per surface. Never restart one.
- Failures back off by doubling, to a cap, and reset on the first success. A `429` is a backoff
  signal like any other: the API's global limiter is keyed on remote IP, so where the API does not
  trust the BFF as a proxy every console shares one bucket.
- Age is measured on a **monotonic** clock (`performance.now()`, `Stopwatch`), never the wall clock —
  which is corrected at exactly the moment "resumed" fires. A negative age counts as stale.
- One scheduler per client with a jittered heartbeat, not N timers, so consoles opened together do
  not tick together.

## The pulse, and the seam for the push channel

`GET /api/v1/dealers/me/pulse` answers one opaque token. The console re-reads its real endpoints when
it differs and does nothing when it does not.

It is an **invalidation signal, not a source of truth**. Bookings, counts, notifications and the
dashboard all keep coming from the endpoints they already came from. No screen renders the token, and
it carries no figure any screen could render — because a count arriving from two places is a count
that can disagree with itself, and the dealer would have no way to tell which was true.

The token is derived at read time from what a dealer's queue can be wrong about: **booking status,
lapse, and the live-dispute flag**. It is not a stored version that writers must remember to bump —
there is no bump to miss. But it only watches those three things, and that list is written down in
`DealerQueueSignature` precisely so that anything which starts changing a row without changing one of
them is added there too.

**When the push channel lands** it calls the same entry point with the same scope names and nothing
else: `touch('bookings')`, `touch('notifications')`, `touch('dealer')`. It carries no payload, and on
reconnect it fires trigger A for every scope, because a socket that was down may have missed
anything. That is what keeps it a faster way to learn the same fact rather than a second way to learn
a different one.

## What this policy does not do

- It does not cache live data. Nothing here makes a surface staler than it is today.
- It does not make the client a second source of truth for anything. Every figure still comes from
  the endpoint that owns it.
- It does not re-judge a past booking. Frozen terms stay frozen; a refresh re-reads, it never
  recomputes.
