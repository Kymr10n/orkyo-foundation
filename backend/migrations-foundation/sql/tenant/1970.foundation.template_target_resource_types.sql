-- @migration-class: expand

-- Lets a request template say what kinds of resource the requests made from it need.
--
-- A request carries its target types in request_target_resource_types (migration 1720). The
-- template a request is created from carries a duration and criterion values only, so
-- "apply template" could never fill the one field that decides which pool the scheduler
-- draws from. A routing step (next migration) is a template used as an operation — "Mill",
-- "Deburr" — and an operation without a machine type is not an operation.
--
-- Same shape as the request-side table: a join table, one row per type, RESTRICT on the type
-- so a type still named by a template cannot be deleted out from under it. Meaningful only
-- for entity_type = 'request'; the repository refuses rows for space and group templates,
-- because that rule reads entity_type from the parent row.
--
-- Rollback: drop the table. Nothing else changes shape.

BEGIN;

CREATE TABLE IF NOT EXISTS public.template_target_resource_types (
    template_id      UUID NOT NULL REFERENCES public.templates(id)      ON DELETE CASCADE,
    resource_type_id UUID NOT NULL REFERENCES public.resource_types(id) ON DELETE RESTRICT,
    created_at       TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT template_target_resource_types_pkey PRIMARY KEY (template_id, resource_type_id)
);

COMMENT ON TABLE public.template_target_resource_types IS
    'The resource types a request made from this template needs, one row per type. Copied '
    'onto request_target_resource_types when the template is applied.';

COMMIT;

-- "Which templates want this type" is what the type-deletion check asks; the PK serves the
-- other direction. After COMMIT so it can be CONCURRENTLY.
CREATE INDEX CONCURRENTLY IF NOT EXISTS idx_template_target_types_type
    ON public.template_target_resource_types (resource_type_id);
