-- Read-only. Answers "is there an administrator, who is it, and can they sign in?"
--
-- Paste into Supabase's SQL editor. It SELECTs and nothing else: no INSERT, no UPDATE, no
-- password hash and no token value is returned by any query here. The one destructive
-- statement on this page is commented out at the bottom and says when to use it.
--
-- Why this exists: the platform deliberately reveals none of this over HTTP. Forgot Password
-- answers the same 202 whether or not the account exists, because anything else turns that
-- form into a way to ask who holds an account. So the database is the only honest place to
-- check, and this is the query to check it with.

-- ─────────────────────────────────────────────────────────────────────────────
-- 1. Every administrator, and whether each can actually sign in.
-- ─────────────────────────────────────────────────────────────────────────────
--
-- can_sign_in mirrors User.CanAuthenticate exactly: deleted, suspended, or an unverified
-- address all refuse. An administrator row is NOT the same thing as a usable login, and the
-- difference is the whole reason a bootstrap can appear to have worked and left nobody able
-- to get in.
--
-- Read is_email_verified first. false means the invitation was never accepted, and a password
-- reset will NOT fix it -- resetting changes the password without verifying the address, so
-- sign-in still fails. Only the invitation link does both.
SELECT
    email,
    role,
    status,
    is_email_verified,
    is_deleted,
    created_at,
    CASE
        WHEN is_deleted            THEN 'NO - soft-deleted'
        WHEN status = 'Suspended'  THEN 'NO - suspended'
        WHEN NOT is_email_verified THEN 'NO - invitation never accepted (a password reset will NOT fix this)'
        ELSE 'yes'
    END AS can_sign_in
FROM users
WHERE role = 'Admin'
ORDER BY created_at;

-- Nothing at all here means the platform has never had an administrator, so the bootstrap
-- will run on the next start. Any row here means it will NOT: the bootstrap only ever creates
-- the FIRST one, and every other administrator is invited from inside the console.

-- ─────────────────────────────────────────────────────────────────────────────
-- 2. Is the address you intend to bootstrap already taken?
-- ─────────────────────────────────────────────────────────────────────────────
--
-- Run this BEFORE setting Admin__Bootstrap__Email. If the address already belongs to anybody
-- -- a customer account made while testing the phone app, say -- the bootstrap cannot use it:
-- the unique index on users.email covers soft-deleted rows too. The API now refuses to start
-- and says so rather than reporting a phantom race, but it is cheaper to know first.
--
-- Replace the address on the next line. Stored addresses are lower-cased on write, and the
-- column is case-sensitive in Postgres, so lower() is how you find one entered oddly.
SELECT email, role, status, is_email_verified, is_deleted
FROM users
WHERE lower(email) = lower('az.arabiat3@gmail.com');

-- ─────────────────────────────────────────────────────────────────────────────
-- 3. Outstanding invitation and reset links.
-- ─────────────────────────────────────────────────────────────────────────────
--
-- Only the HASH is stored, never the link, so this cannot show you a link to click -- it can
-- only tell you whether a live one is out there. The bootstrap declines to send a second
-- invitation while the first is live, because a new one invalidates the old.
SELECT
    t.purpose,
    u.email,
    t.created_at,
    t.expires_at,
    t.consumed_at,
    CASE
        WHEN t.consumed_at IS NOT NULL THEN 'used'
        WHEN t.expires_at < now()      THEN 'expired'
        ELSE 'LIVE - this link still works'
    END AS state
FROM verification_tokens t
JOIN users u ON u.id = t.user_id
WHERE t.purpose IN ('AdminInvitation', 'EmployeeInvitation', 'PasswordReset', 'EmailVerification')
ORDER BY t.created_at DESC
LIMIT 50;

-- ─────────────────────────────────────────────────────────────────────────────
-- 4. ONLY IF the mail transport was writing messages to the log.
-- ─────────────────────────────────────────────────────────────────────────────
--
-- Commented out deliberately: run it only after reading what it does.
--
-- While Email:Provider selected the Logging transport, every message was written to the
-- application log INCLUDING ITS LINK, and each of those links is a working credential until
-- its token expires -- an hour for a reset, a day for verification, SEVEN DAYS for an
-- invitation. A logged administrator invitation is a seven-day credential to become the
-- platform administrator, sitting in a log that more people can read than should hold one.
--
-- This consumes every live link, so none of them can be used. Nobody loses anything: they ask
-- again and get a fresh one. Do NOT run it while the Logging transport is still selected --
-- the replacement would be written straight back into the same log.
--
-- UPDATE verification_tokens
-- SET consumed_at = now()
-- WHERE consumed_at IS NULL
--   AND expires_at > now();
