-- @migration-class: data

-- Rename the legacy 'planned' status token to 'new' on all existing rows, and flip the
-- column DEFAULT so inserts that omit status land on 'new'. Old code (which writes
-- 'planned') still validates because 1610 left 'planned' in the CHECK, so this remains
-- rollback-safe to the 1610 state.

UPDATE public.requests SET status = 'new' WHERE status = 'planned';

ALTER TABLE public.requests
    ALTER COLUMN status SET DEFAULT 'new'::character varying;
