-- @migration-class: expand

-- An optional idempotency key for outbox rows. Announcement broadcasts enqueue one row per
-- recipient as "announcement:<announcement id>:<user id>"; a broadcast interrupted by a
-- deploy or a crash is re-run, and the unique index turns the second enqueue of the same
-- recipient into a no-op instead of a second mail. Rows without a key keep NULL, which the
-- unique index does not constrain.

BEGIN;

ALTER TABLE public.email_outbox
    ADD COLUMN IF NOT EXISTS dedupe_key text;

COMMIT;

-- CONCURRENTLY cannot run inside a transaction; the runner executes this in autocommit.
CREATE UNIQUE INDEX CONCURRENTLY IF NOT EXISTS idx_email_outbox_dedupe_key
    ON public.email_outbox (dedupe_key)
    WHERE dedupe_key IS NOT NULL;
