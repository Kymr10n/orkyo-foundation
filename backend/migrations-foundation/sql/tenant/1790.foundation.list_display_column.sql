-- @migration-class: expand

-- The column that identifies a row.
--
-- 1780 gave a list definition its columns but nothing that says which of them names a row. Every
-- surface that has to show a row as a single value therefore guessed: the row picker took the
-- first active column and appended the rest as context, so a component read "Name — 7'865"
-- where its name should be. The author of a list knows which column identifies a row; this is
-- where they say so.
--
-- On the definition rather than as a flag on the column: one definition has exactly one display
-- column, and a single nullable FK cannot drift into two columns both claiming to be it. The
-- alternative (list_columns.is_display) would need a partial unique index to say the same thing
-- and could still be half-updated between two statements.
--
-- ON DELETE SET NULL, not CASCADE: deleting the designated column must not delete the definition
-- and every row built from it. It falls back instead — see below.
--
-- Nullable, with no backfill: unset means "fall back to the first active column", which is
-- exactly what the code does today. Every existing definition therefore keeps its current
-- behaviour, and the fallback stays the answer for a definition whose display column is later
-- deactivated or deleted.
--
-- The FK is circular with list_columns.list_definition_id, which Postgres allows because this
-- side is nullable: a definition is inserted first with NULL here, its columns next, and the
-- designation last.
--
-- Rollback: see the matching revert script (drops the column; the designation is not recoverable).

BEGIN;

ALTER TABLE public.list_definitions
    ADD COLUMN IF NOT EXISTS display_column_id UUID;

ALTER TABLE public.list_definitions
    DROP CONSTRAINT IF EXISTS list_definitions_display_column_fkey;

ALTER TABLE public.list_definitions
    ADD CONSTRAINT list_definitions_display_column_fkey
        FOREIGN KEY (display_column_id) REFERENCES public.list_columns(id) ON DELETE SET NULL;

-- The service checks that the column belongs to this definition, which the database cannot
-- express in a foreign key. This index is what makes that check, and the SET NULL above, cheap.
CREATE INDEX IF NOT EXISTS idx_list_definitions_display_column
    ON public.list_definitions (display_column_id)
    WHERE display_column_id IS NOT NULL;

COMMIT;
