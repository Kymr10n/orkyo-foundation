-- @migration-class: expand

-- List definitions gain an owner.
--
-- 1780 made a definition tenant-global: one flat namespace, unique by name, with no statement
-- of who it belongs to. That was enough while every definition was reached through a custom
-- field, because the field's resource type supplied the context. It stops being enough once
-- definitions get their own surfaces, where "Certification" on a mill and "Certification" on a
-- person are different vocabularies that must not collide, and where departments and job titles
-- are organization data rather than the property of any one type.
--
-- Three scopes, one column:
--
--   * 'resource'     — owned by one resource type. Edited from that type's Lists tab.
--   * 'organization' — organization master data (departments, job titles, cost centres).
--   * 'common'       — shared reference data (countries, units) with no narrower owner.
--
-- An explicit column rather than inference from resource_type_id: a null owner cannot tell
-- 'organization' from 'common', so the column is needed regardless, and one source of truth
-- with a CHECK beats two half-signals that can disagree.
--
-- The paired CHECK keeps the two columns honest in both directions — a resource-scoped
-- definition must name its type, and the other two scopes must not.
--
-- Uniqueness moves from the bare name to (scope, resource_type_id, name). Postgres treats
-- NULLs as distinct in a unique constraint, which would let two 'common' definitions share a
-- name, so the two owner-less scopes get a partial unique index on (scope, name) instead and
-- the resource scope gets its own over the non-null column.
--
-- Everything that exists today becomes 'common': it was created before ownership existed, so
-- no narrower claim is defensible, and 'common' is the scope that changes no behaviour. The
-- demo seed re-declares its own definitions with the right scope.

BEGIN;

ALTER TABLE public.list_definitions
    ADD COLUMN IF NOT EXISTS scope            TEXT NOT NULL DEFAULT 'common',
    ADD COLUMN IF NOT EXISTS resource_type_id UUID NULL;

ALTER TABLE public.list_definitions
    DROP CONSTRAINT IF EXISTS list_definitions_name_unique;

ALTER TABLE public.list_definitions
    ADD CONSTRAINT list_definitions_scope_check
        CHECK (scope IN ('resource', 'organization', 'common')),
    ADD CONSTRAINT list_definitions_scope_owner_check
        CHECK ((scope = 'resource') = (resource_type_id IS NOT NULL)),
    -- A type that is deleted takes its own definitions with it. Deleting a type is already
    -- refused while resources reference it, so this only fires for a type nothing uses.
    ADD CONSTRAINT list_definitions_resource_type_fkey
        FOREIGN KEY (resource_type_id) REFERENCES public.resource_types(id) ON DELETE CASCADE;

COMMIT;

-- CONCURRENTLY cannot run inside a transaction. The runner does not wrap scripts, so
-- statements after COMMIT run in autocommit (the 1720 precedent).
CREATE UNIQUE INDEX CONCURRENTLY IF NOT EXISTS ux_list_definitions_resource_name
    ON public.list_definitions (resource_type_id, name)
    WHERE resource_type_id IS NOT NULL;

CREATE UNIQUE INDEX CONCURRENTLY IF NOT EXISTS ux_list_definitions_global_name
    ON public.list_definitions (scope, name)
    WHERE resource_type_id IS NULL;

CREATE INDEX CONCURRENTLY IF NOT EXISTS idx_list_definitions_scope
    ON public.list_definitions (scope);
