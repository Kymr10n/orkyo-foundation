-- @migration-class: contract
--
-- Drops the sort_order column from departments. The column was included in the
-- initial schema (migration 1420) but removed before any feature code shipped
-- that used it. Ordering is handled by name (alphabetical) at the query layer.

-- IF EXISTS makes this a no-op on environments that received the edited 1420
-- (no sort_order ever created) while still cleaning up any environment that
-- ran the pre-edit 1420 (sort_order present and needing removal).
ALTER TABLE public.departments DROP COLUMN IF EXISTS sort_order;
