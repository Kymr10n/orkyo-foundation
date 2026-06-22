-- @migration-class: contract

-- Drop tenant-database indexes that are fully covered by an existing UNIQUE/PK
-- backing index (or a wider composite index), so they only add write-amplification
-- and bloat.
--
--   idx_criteria_name            → covered by criteria_name_key UNIQUE (name)
--                                  (lookups are further served by the case-insensitive
--                                   unique idx_criteria_name_lower)
--   idx_requests_start_ts        → left-prefix of idx_requests_time_range (start_ts, end_ts)
--   idx_memberships_user_id      → left-prefix of unique (user_id, site_id)
--   idx_user_preferences_user_id → covered by user_preferences_pkey (user_id)
--
-- The case-sensitive criteria_name_key UNIQUE constraint is intentionally LEFT IN
-- PLACE here — collapsing it into the case-insensitive unique is a separate decision
-- (see docs/data-model-schema-review-2026-06.md §B2), not part of this cleanup.
--
-- Rollback: recreate the dropped indexes (all non-unique btrees on the listed columns).

BEGIN;

DROP INDEX IF EXISTS public.idx_criteria_name;
DROP INDEX IF EXISTS public.idx_requests_start_ts;
DROP INDEX IF EXISTS public.idx_memberships_user_id;
DROP INDEX IF EXISTS public.idx_user_preferences_user_id;

COMMIT;
