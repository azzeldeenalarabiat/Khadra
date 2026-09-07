# Spec amendments

Decisions the owner has made since `Car_Rental_System_v3.1.docx` was written, each superseding a
numbered section of it.

The `.docx` is kept as the historical record and is **not** edited: a Word file cannot be reviewed in
a diff, and an automated edit to one loses tables and styles. This file is the current word. Where
the two disagree, this file wins, and every entry names exactly which section it replaces so nobody
has to guess which document they are reading.

Entries are newest first.

---

## 2026-09-07 — Reserve now, pay after approval

**Supersedes:** §5.3 "Payment at Booking", and the booking state order implied by §2 and §3.1.

### What changed

The customer used to pay the 20% deposit **before** the dealer ever saw the request. They no longer
do. The order is now:

1. The customer submits a request. **No payment.**
2. The dealer or their employee approves or rejects it.
3. On approval the customer has **24 hours** to pay the deposit.
4. Unpaid within that window, the booking expires and the car returns to the market.

**The deposit requirement is unchanged.** Everything §5.3 says about the money itself still holds:
20% of the total by card, or 100% upfront; the remaining 80% in cash to the dealer at pickup; and the
deposit is still what carries the platform's commission (§2.1). A booking without it is not a
booking. Only *when* it is collected has moved.

### The deadline is immediate

An unpaid booking ends **the moment the window closes**, not whenever a job next happens to look. That
is a requirement, not a nicety: the car has to return to the market at the deadline, or the platform
is holding stock nobody has paid for.

It is met in two halves, and the first is what makes it immediate:

- **The car stops being held at the deadline itself.** The vehicle-hold predicate counts an
  `Approved` booking only while `PaymentDeadline > now`, exactly as it once counted `PendingPayment`.
  So the instant the deadline passes, the search stops hiding the car and the overlap guard stops
  refusing it — with no job involved.
- **A background job then settles the status**, moving the booking to a terminal state and telling
  both parties. It is tidying up after a decision the clock has already made.

The database's exclusion constraint cannot see the current time, so it still treats a stale unpaid
hold as live. Whatever creates a booking must therefore expire stale ones on that vehicle in the same
transaction before inserting; this is pre-launch checklist item 53.

### Why the domain calls it "expired" rather than "cancelled"

Both words describe the same outcome — the booking is over and the car is free. The domain
distinguishes them, and the distinction is worth keeping: `Cancelled` is a PARTY deciding to end a
booking, and carries who decided and what they owe for it; `Expired` is a window closing with nobody
having decided anything. An unpaid deadline is the second. It also reads better to the customer, who
should be able to tell "you cancelled this" from "the time ran out" from "the gallery declined".

Nothing is owed either way: no money has moved, so there is nothing to assess a penalty against.

### The lifecycle

```
Requested -> Approved -> Confirmed -> PickedUp -> Returned -> Completed
   exits:  Rejected, Cancelled, Expired, NoShow
```

`PendingPayment` is gone. Its job — holding the car while money was arranged — now belongs to
`Approved`. `Confirmed` is new and means the deposit has been paid.

`Confirmed` is a state rather than a flag on `Approved` because the two are different things to
everyone who reads them: the customer is told "we are waiting for your payment" or "you are booked",
the expiry job looks for approved-and-unpaid, and the vehicle-hold predicate has to name both.

### Why

It lets the request-and-approval loop exist before Payments does. Under the old order nothing could
reach a dealer at all without a working card rail, so the whole platform waited on the module that is
blocked on the most owner decisions.

It is also the better flow on its own merits. A customer is no longer asked for money before anyone
has agreed to rent them a car, and a dealer sees a real request rather than a paid one they must
honour or refund.

### What it cost, and what was done about it

**A request now holds a car for nothing.** The deposit used to be the friction: you paid before the
car was held. Two things replace it.

First, an unanswered request now expires after the **dealer approval SLA of 48 hours** (§3.1). The
code previously let a request live until the rental period began, which was harmless while a deposit
gated it — a customer could otherwise have held a car for the full 180-day booking horizon for free.
This makes the platform keep a promise §3.1 already made.

Second, a customer must have **uploaded a driving licence and an ID or passport** before they may
request anything (§5.1). Not admin-verified: nothing can move a document out of `PendingReview` yet,
so requiring verification would mean nobody could book at all.

The answer window is its own setting rather than the admin SLA it happens to equal. They are
different clocks owned by different people — one is how long an administrator may take over a
gallery's licence, the other how long a gallery may leave a customer waiting — and sharing a key
would mean the owner could not move one without moving the other.

### Numbers

| Setting | Value | Where |
|---|---|---|
| `BusinessRules:PaymentWindowHours` | 24 | Frozen onto each booking as `BookingTerms.PaymentWindow`; published on `GET /api/v1/app-config` |
| `BusinessRules:BookingAnswerWindowHours` | 48 | Frozen onto each booking as `BookingTerms.AnswerWindow`; expires an unanswered request |

**24 hours, not the one hour first proposed.** There are no push notifications yet, so a customer
learns of an approval only by opening the app. A one-hour window would auto-cancel most bookings
approved overnight or during working hours before the customer ever saw them, and waste the dealer's
decision. Shorten it once push exists — it is one configuration value, which is why it is not a
constant. Tracked on the pre-launch checklist.

The window is capped at the rental start: a booking approved twenty minutes before pickup cannot have
a 24-hour window.

### Free cancellation

§5.5 measures the free-cancellation window from **approval**. It now runs from **payment**, because
at approval nothing has been paid and there is nothing to be penalised on. The duration is unchanged.

### Owner decisions on the exposure this creates (2026-09-07)

Three consequences were put to the owner and accepted as they stand.

**A car can be held free for 72 hours, and the hold is renewable.** The two windows compose: 48 hours
for the dealer to answer, then 24 for the customer to pay. Nothing has been paid for any of it. And
nothing stops the same customer requesting the same car again the moment their request expires, so
the ceiling is 72 hours per request, not per customer. Accepted as the exposure of launching without
Payments; the deposit was what used to make hoarding expensive, and the two windows are what replace
it. Tracked as a checklist item so it is reconsidered rather than forgotten.

**Cancelling before the deposit clears costs nothing, whoever cancels.** No money has moved, so there
is nothing a penalty could be assessed against — this is the domain's existing rule and it now covers
an approved-but-unpaid booking as well as an unanswered request. A dealer can therefore approve and
the customer walk away at no cost, and a customer can hold a car for 72 hours and drop it. Accepted
for now; to be revisited once Payments exists and there is something to charge against.

**Identity documents are a checkbox, not a check.** A customer must have UPLOADED a licence and an
identity document before they may request a car; nobody looks at them. Nothing can move a document
out of `PendingReview`, and no endpoint lets a dealer open one, so the dealer's spec 5.1 duty to
check a renter's licence cannot be performed on the platform at all. The owner has recorded the
dealer document-view endpoint as a HARD requirement before real launch, not a nice-to-have: without
it the platform is handing out cars on an unverified claim.

### What is built (2026-09-07)

`POST /api/v1/bookings` exists and the request half of the loop works end to end: a customer
registers, verifies their email, uploads a licence and an identity document, browses, is quoted, and
asks a gallery for a car. The booking holds the vehicle, the catalogue stops offering it, and a
second request for the same dates is refused.

Two rules were added with it, both configured:

| Setting | Value | Why |
|---|---|---|
| `BusinessRules:MinimumBookingLeadTimeMinutes` | 120 | Every window on a booking is capped at the rental start, so without a floor they all collapse together on a last-minute request while the platform advertises a 24-hour payment window. Published on `/app-config` for the date picker. |
| `BusinessRules:MaxAdvanceBookingDays` | 180 | Already existed and was published but never enforced. Now refused at creation. |
| `BusinessRules:MaxRentalDays` | 90 | Nothing bounded the far end: a five-year request was accepted and held a car for the whole answer window. Judged on the BILLED day count, so the figure a customer is refused on is the one they were quoted. |

**Email verification is required before booking.** Until push notifications exist, an approval reaches
a customer only by email; an unverified address is a booking that expires unread, with the gallery's
decision wasted and the car held for nothing meanwhile.

**A self-pickup must fall inside the gallery's opening hours**, at both ends; a delivery is never
judged against counter hours. Settled by the owner on 2026-09-07 — see pre-launch checklist item 66.

**The gallery is told when a request arrives**, in the same transaction that creates it, and the
notification names no customer — see pre-launch checklist item 67 for why. The customer is NOT yet
told when their booking is approved and the deposit falls due; that is item 60 and it is the thing
that most needs building next.

### Still open

Payments remains unbuilt and blocked on owner decisions (see `docs/architecture-bounded-contexts.md`).
Until it ships, a booking can be requested, approved and expired, but not paid — so `Confirmed` is
reachable only in tests. The customer app shows the payment step as a screen that says so, rather
than a button that cannot work.
