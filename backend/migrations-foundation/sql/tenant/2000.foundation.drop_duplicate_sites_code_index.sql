-- @migration-class: contract

-- Drop idx_sites_code (1140). The UNIQUE constraint sites_code_key already backs an index
-- on sites (code), so the planner never needs the plain btree; it only adds write
-- amplification and bloat. Same reasoning as the control-plane 1140 drop of indexes that
-- duplicate a UNIQUE constraint. Rollback: recreate the non-unique btree on sites (code).

BEGIN;

DROP INDEX IF EXISTS public.idx_sites_code;

COMMIT;
