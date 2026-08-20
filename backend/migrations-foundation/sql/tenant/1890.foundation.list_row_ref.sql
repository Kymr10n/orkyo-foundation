-- @migration-class: expand

-- A list column can hold the id of another row of the same list.
--
-- 1820 moved departments into a list and said what that cost: "the tree is no longer enforced",
-- with the parent kept as free text so no information disappeared from view. This is the type
-- that gives the tree back — one column type, `row_ref`, not a departments-shaped special case,
-- because "this row sits under that row" is a sentence any list can want to say.
--
-- The database still does not enforce the reference. Every row of every instance lives in one
-- table, so a foreign key on `list_rows.values` could not tell a sibling row from a row of some
-- other instance, and a cycle is not a constraint at all. ListRowService checks all three on
-- write: the target exists in this instance, it is not the row itself, and the chain above it
-- does not come back. What this migration adds is the vocabulary; 1900 converts the departments.

BEGIN;

-- Widen the type CHECK by same-name drop and re-add (the technique 1610 established and 1780
-- reused). Nothing else moves: `list_columns_options_only_for_select` already rejects options on
-- anything that is not `select`, which is exactly right for a row_ref column.
ALTER TABLE public.list_columns
    DROP CONSTRAINT IF EXISTS list_columns_data_type_check;

ALTER TABLE public.list_columns
    ADD CONSTRAINT list_columns_data_type_check
        CHECK (data_type IN ('text', 'number', 'boolean', 'date', 'url', 'select', 'row_ref'));

COMMIT;
