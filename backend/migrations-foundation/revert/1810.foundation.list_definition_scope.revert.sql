-- Reverts 1810. Ownership disappears and the flat name namespace comes back.
--
-- The name uniqueness this restores is stricter than what 1810 allows, so the revert fails when
-- two definitions in different scopes share a name. That is the correct outcome: dropping one of
-- them is a data decision, not something a revert script decides on a tenant's behalf. The
-- transaction matters here: the migrator runs scripts without one (DbUp defaults to
-- NoTransaction), so without BEGIN a failed constraint would leave the columns already dropped.

BEGIN;

DROP INDEX IF EXISTS public.list_definitions_scope_idx;
DROP INDEX IF EXISTS public.list_definitions_global_name_unique;
DROP INDEX IF EXISTS public.list_definitions_resource_name_unique;

ALTER TABLE public.list_definitions
    DROP CONSTRAINT IF EXISTS list_definitions_resource_type_fkey,
    DROP CONSTRAINT IF EXISTS list_definitions_scope_owner_check,
    DROP CONSTRAINT IF EXISTS list_definitions_scope_check;

ALTER TABLE public.list_definitions
    DROP COLUMN IF EXISTS resource_type_id,
    DROP COLUMN IF EXISTS scope;

ALTER TABLE public.list_definitions
    ADD CONSTRAINT list_definitions_name_unique UNIQUE (name);

COMMIT;
