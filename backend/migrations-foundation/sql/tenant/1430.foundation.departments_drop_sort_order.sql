-- @migration-class: contract
--
-- Drops the sort_order column from departments. The column was included in the
-- initial schema (migration 1420) but removed before any feature code shipped
-- that used it. Ordering is handled by name (alphabetical) at the query layer.

ALTER TABLE public.departments DROP COLUMN sort_order;
