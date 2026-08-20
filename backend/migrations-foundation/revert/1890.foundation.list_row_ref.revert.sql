-- Reverts 1890. The row_ref column type stops being sayable.
--
-- This fails while any column still declares `row_ref` — the narrower CHECK cannot be added over
-- rows that violate it. That is the correct outcome: dropping such a column deletes a tenant's
-- structure, which is a data decision and not something a revert script makes on their behalf.
-- Revert 1900 first where it ran. The transaction matters: the migrator runs scripts without one
-- (DbUp defaults to NoTransaction), so without BEGIN a failed re-add would leave no CHECK at all.

BEGIN;

ALTER TABLE public.list_columns
    DROP CONSTRAINT IF EXISTS list_columns_data_type_check;

ALTER TABLE public.list_columns
    ADD CONSTRAINT list_columns_data_type_check
        CHECK (data_type IN ('text', 'number', 'boolean', 'date', 'url', 'select'));

COMMIT;
