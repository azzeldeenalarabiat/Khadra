# Release 2026-09 — bilingual office texts, and the app version gate

**Status: deployed and verified (owner, 2026-09-23).** Production API and BFF run `d9a3237`
(tag `v1.1.0`); GitHub release `v1.1.0` is marked latest. Confirmed from outside on 2026-09-23:
`/app-config` publishes `mobileApp.minimumSupportedVersion = 1.1.0` and `payments.mode = None`,
and a request stamped `X-Khadra-App-Version: 1.0.0+1` is refused with `426`. The package below is
kept as the record of how it went out and how to get back.

Before this release production ran `a6cc71a`. This release is every commit after it, up to `d9a3237`.

---

## What ships

| | |
|---|---|
| **API** (`khadra`) and **console** (`khadra-bff`) | 22 commits: the lapse seam and the replayed-webhook fix (`1b822d6`…`707580e`), the sandbox payment provider (`339072f`…`64f333d`, which stays switched off in production), Batch B (`f763fd5`…`9d810aa`), the contract rule (`e61bbfe`), release signing (`e689dda`), and this package |
| **Schema** | [../sql/2026-09-22-bilingual-dealer-content.sql](../sql/2026-09-22-bilingual-dealer-content.sql) — `ChildCollectionsDeleteTheirOrphans` if production never had it, then `BilingualDealerContent` |
| **Configuration** | Nothing to set by hand. `MobileApp:MinimumSupportedVersion` (1.1.0) and `MobileApp:UpdateUrl` ship in `appsettings.json` |
| **The app** | Android 1.1.0+2, published as a GitHub release |

What changes for people: a rental office writes its page in Arabic and English and each
customer reads their own language; the dealer console refreshes itself instead of waiting
for F5; a customer's bookings list stops spinning; and every customer-app build older than
1.1.0 is refused with an update message rather than breaking on the new shapes.

## The APK

| | |
|---|---|
| File | `C:\Users\azara\OneDrive\Desktop\khadra-production-e689dda.apk` |
| Built from | `e689dda`, with `--dart-define=KHADRA_API_BASE_URL=https://khadra.onrender.com` |
| Version | 1.1.0, versionCode 2 (`X-Khadra-App-Version: 1.1.0+2`) |
| Size | 65,106,827 bytes |
| SHA-256 | `a4600235ea37ea58d6c961c0072510f103e6eb5b70e11df11e47c6b52291d282` |
| Signed with | The release key — `CN=Khadra, O=Khadra, L=Amman, C=JO`, certificate SHA-256 `AD:62:F2:E9:3B:AB:9E:55:5E:B2:D6:5D:44:70:31:0A:68:42:1A:D5:D6:B7:46:37:7D:9E:2D:31:AF:28:EF:C4` |

Checked on 2026-09-22 against the **live** production API, before any migration: installs,
opens on Get Started, browses as a guest, lists the four cars, opens a car page with its
photos, opens an office page, switches to Arabic and back. Nothing in the log. The office
texts do not appear, which is the designed behaviour against an API that still sends them
as plain strings.

**It cannot install over 1.0.0.** Measured: `INSTALL_FAILED_UPDATE_INCOMPATIBLE: Existing
package com.khadra.khadra_mobile signatures do not match newer version`, and 1.0.0 stays.
Uninstall, then install, works. On a phone the refusal reads *"App not installed as package
conflicts with an existing package"*. Every 1.0.0 was signed with a laptop's debug key;
1.1.0 is the first signed with Khadra's own (pre-launch item 134).

## What is on phones today

Found here, both 1.0.0, both debug-signed, both pointing at `https://khadra.onrender.com`,
and both sending `Khadra (Android …)` — so the gate can refuse them:

| File | Built | SHA-256 |
|---|---|---|
| `Desktop\khadra-production-a6cc71a.apk` | 2026-09-21 16:25 | `9c5bda319952ec58a7cb6d2e261ae7a958b8de6076b838e67ec8e85150b4d66e` |
| `Khadra.Mobile\build\app\outputs\flutter-apk\app-release-1.0.0-built-2026-09-21-1641.apk` | 2026-09-21 16:41 | `ae6b0747ea9f029a6f54d25496110fe910b2500e1a70ddf3f1af6c34854ae6bb` |

There are no GitHub releases and no other APK on this machine. A build from **before
2026-09-21 05:01** (`d6fddef`) sends Dart's default User-Agent, cannot be told from any
other client, and so cannot be refused: it would break on car and office pages instead of
being told to update. Whether one is still in use is the first pre-flight query below.

---

## Before the rollout — nothing here changes production

1. **Which builds have signed in.** Supabase → SQL editor, read-only:

   ```sql
   select
     case
       when t.user_agent like 'Khadra (%' then 'app from 2026-09-21 on: refusable'
       when t.user_agent like 'Dart/%'    then 'app built before 2026-09-21: NOT refusable'
       when coalesce(t.user_agent, '') = '' then 'no user agent recorded'
       else 'other client'
     end                                                                    as kind,
     t.user_agent,
     count(distinct t.family_id)                                            as sessions_ever,
     count(distinct t.family_id) filter (where t.revoked_at is null
                                           and t.family_expires_at > now()) as sessions_live,
     count(distinct t.user_id)                                              as accounts,
     max(t.created_at)                                                      as last_seen
   from refresh_tokens t
   join users u on u.id = t.user_id
   where u.role = 'Customer'
   group by 1, 2
   order by last_seen desc;
   ```

   A `Dart/…` row with a live session means somebody is carrying a build the gate cannot
   refuse: message them directly, because the API cannot. A guest who never signed in leaves
   no row at all, so ask the testers too.

2. **Which migrations production has.** Read-only:

   ```sql
   select migration_id from "__EFMigrationsHistory" order by migration_id desc limit 3;
   ```

   Expect `20260920233804_ChildCollectionsDeleteTheirOrphans` or
   `20260918201534_PenaltyReasonCode` on top. Anything else: stop, and work out what else
   is pending before going on.

3. **Render → `khadra` → Environment.** `Payments__Provider` must be absent or `None`: the
   new build validates it and refuses to start on a value it does not know, which the
   running one never did. No `MobileApp__*` variable should be set; the values ship in the
   image.

4. **Render → `khadra` and `khadra-bff` → Settings → Auto-Deploy: Off.** Note which branch
   each is linked to. This is what lets the merge happen without deploying.

5. **Publish the APK** (needs the owner: this is public). Upload it as `khadra.apk` —
   that exact name is what `…/releases/latest/download/khadra.apk` resolves to:

   ```bash
   cp "/c/Users/azara/OneDrive/Desktop/khadra-production-e689dda.apk" /tmp/khadra.apk
   gh release create mobile-v1.1.0 /tmp/khadra.apk --latest \
     --target <the commit main will point at> \
     --title "Khadra 1.1.0 (Android)" --notes-file docs/releases/2026-09-notes.md
   ```

6. **Tell the testers, before the API changes:** uninstall the old Khadra first, then install
   this one, then sign in again. Both because Android refuses the update (different key) and
   because the old build clears its own session when the new API refuses it.

---

## The rollout

Each step says what to see before going on. Times are what to expect on Render's free plan.

### 1. Stop everything old

- Render → **`khadra-bff`** → **Suspend**. The console goes down first and stays down
  until the new API is live. That is what makes an old API process harmless if one runs:
  the console is the only thing that writes an office's texts.
- Render → **`khadra`** → **Suspend**.
- Check: `curl -s -o /dev/null -w "%{http_code}\n" https://khadra.onrender.com/health/live`
  no longer answers 200.

### 2. Back up

The connection string is the Supabase **session pooler** one. Read it into the shell without
putting it in history, and keep the dump off the repository:

```bash
read -rs PGURL && export PGURL
docker run --rm -e PGURL -v "$PWD:/backup" postgres:18-alpine sh -c \
  'pg_dump "$PGURL" --schema=public --format=custom --no-owner --no-privileges \
     --file=/backup/khadra-before-bilingual.dump'
docker run --rm -v "$PWD:/backup" postgres:18-alpine sh -c \
  'pg_restore --list /backup/khadra-before-bilingual.dump | grep -c "TABLE DATA"'
```

Check: the second command prints a table count, and the file is not empty. Documents live in
Supabase Storage and are untouched by this release.

### 3. Migrate

Run it **from the file**, not pasted into the SQL editor — the script detector's character
ranges are literal Arabic, and a paste that normalises them fails the self-test:

```bash
docker run --rm -i -e PGURL postgres:18-alpine sh -c 'psql "$PGURL" -v ON_ERROR_STOP=1' \
  < docs/sql/2026-09-22-bilingual-dealer-content.sql
```

Check: exit code 0. On any failure, nothing of `BilingualDealerContent` was committed —
it aborts inside its own transaction and says why. Fix and run again; it is idempotent.

### 4. Verify the copy

```sql
with pairs(v, a, e) as (
  select description, description_ar, description_en from dealers
  union all select rental_conditions,   rental_conditions_ar,   rental_conditions_en   from dealers
  union all select insurance_summary,   insurance_summary_ar,   insurance_summary_en   from dealers
  union all select pickup_instructions, pickup_instructions_ar, pickup_instructions_en from dealers
  union all select delivery_notes,      delivery_notes_ar,      delivery_notes_en      from dealers
  union all select customer_notes,      customer_notes_ar,      customer_notes_en      from dealers
  union all select description,         description_ar,         description_en         from vehicles)
select count(*) filter (where v is not null) as legacy,
       count(*) filter (where v is not null and (a is null) <> (e is null)
                          and coalesce(a, e) = v) as verbatim_on_one_side,
       count(*) filter (where v is null and (a is not null or e is not null)) as invented
from pairs;
```

Check: `legacy = verbatim_on_one_side`, and `invented = 0`. Record the three numbers in
pre-launch item 132; that is what closes it.

### 5. Start the new API

- Merge this branch into `main` (fast-forward) and push. Auto-deploy is off, so nothing
  deploys yet.
- Render → `khadra` → **Resume**. If it comes back on the old deploy, that is fine: the
  console is still suspended, so nothing can write an office's texts, and the old build
  reads the legacy columns, which still hold what they held.
- Render → `khadra` → **Manual Deploy → Deploy a specific commit** → the new tip of `main`.
  Build and deploy: roughly 5–10 minutes.

Check, in the boot log: `Email ready…`, `Document storage. …bucket is private.`,
`Forwarded headers trusted from: …`, `PAYMENTS ARE NOT ACCEPTED…`, and

```
Customer app: builds older than 1.1.0 are REFUSED with 426 app.update_required, and so is
any build that sends the app User-Agent without X-Khadra-App-Version. Update link: https://…
```

Then, from anywhere:

```bash
curl -s -o /dev/null -w "ready %{http_code}\n" https://khadra.onrender.com/health/ready
curl -s https://khadra.onrender.com/api/v1/app-config | grep -o '"mobileApp":{[^}]*}'
curl -s -o /dev/null -w "old app %{http_code}\n" -A "Khadra (Android 16)" https://khadra.onrender.com/api/v1/vehicles
curl -s -o /dev/null -w "new app %{http_code}\n" -H "X-Khadra-App-Version: 1.1.0+2" https://khadra.onrender.com/api/v1/vehicles
curl -s -o /dev/null -w "browser %{http_code}\n" -A "Mozilla/5.0" https://khadra.onrender.com/api/v1/vehicles
```

Expect `ready 200`, the published minimum and update link, `old app 426`, `new app 200`,
`browser 200`.

**Then open 1.1.0 on a real phone.** It must load normally, and the log must show no
`Refused an old customer-app build` line for it. If it is refused, the build is not sending
its version: set `MobileApp__MinimumSupportedVersion` to a single space in Render's
environment and save, which turns the gate off within a restart, and investigate before
trying again.

### 6. Serve the new console

- Render → `khadra-bff` → **Resume**, then **Manual Deploy → Deploy a specific commit** →
  the same commit. An old console tab meanwhile is refused with 400 on a customer-page save
  and writes nothing.
- Check: `GET /bff/antiforgery` answers 200; sign in; a dealer's customer page shows an
  Arabic box and an English box, and the preview shows both audiences.

### 7. Afterwards

- Turn Auto-Deploy back on if that is how the services are normally run.
- Watch the API log for a few days: `Refused an old customer-app build (no version header,
  User-Agent "Khadra (…)")` counts phones still on 1.0.0. A `Dart/…` build never appears
  there — it cannot be identified, only broken.
- Close pre-launch item 132 with step 4's numbers. Item 133 is already closed; 131 (dropping
  the seven legacy columns) waits for its own migration, later.

---

## Getting back

| Where it goes wrong | What to do |
|---|---|
| Before step 3 | Resume both services. Nothing has changed |
| Step 3 fails | Nothing of the bilingual migration was committed. `ChildCollectionsDeleteTheirOrphans` may have been, which is harmless and idempotent. Resume both services and the old build runs exactly as before |
| After step 3, before step 5 | The old build runs on the migrated database: the new columns are additive and it neither reads nor writes them. Resume it. Do not let the console back up until you decide, or an office could save a page into columns nothing will read |
| The new API misbehaves | Render → `khadra` → Deploys → **Rollback** to the `a6cc71a` deploy. The old build reads the legacy columns, so any office text edited through the new console since step 5 will not be visible, and the gate stops refusing old apps |
| The new API refuses 1.1.0 | `MobileApp__MinimumSupportedVersion` to a single space, save |
| The database has to go back | Restore the dump from step 2 into a new database and repoint `ConnectionStrings__DefaultConnection`. `Down` on the migration drops the new columns and loses anything written through the bilingual editor since step 3 |

The one thing with no way back is the signing key: see pre-launch item 134.
