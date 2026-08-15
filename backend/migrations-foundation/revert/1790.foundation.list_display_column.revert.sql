-- Revert of 1790: drops the display-column designation.
--
-- The designation itself is lost; nothing else is. Every surface falls back to the first active
-- column, which is what it did before 1790.

BEGIN;

ALTER TABLE public.list_definitions
    DROP CONSTRAINT IF EXISTS list_definitions_display_column_fkey;

DROP INDEX IF EXISTS idx_list_definitions_display_column;

ALTER TABLE public.list_definitions
    DROP COLUMN IF EXISTS display_column_id;

COMMIT;
