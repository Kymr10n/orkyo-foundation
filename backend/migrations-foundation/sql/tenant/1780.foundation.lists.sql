-- @migration-class: expand

-- Lists — tenant-defined tables that hang off a resource. A car's maintenance log, a
-- machine's components, a person's certifications. Mental model: SharePoint lists, but
-- smaller, and always reached through the resource that owns them.
--
-- This extends 1770's custom fields from scalars to repeating structured data, and holds
-- the same boundary 1770 restated from 1680: **never matchable**. No request requirement
-- points at list data, no capability matcher reads it, nothing here reaches the solver. A
-- value that ought to drive an assignment is a criterion and belongs in the criteria
-- system. Lists are descriptive, they render on the resource form and in exports, and
-- that is all they do.
--
-- The shape is definition/instance, both words used literally in the API and the UI:
--
--   * a LIST DEFINITION is the reusable part — the columns and their types ("Maintenance
--     log: date, mileage, note"). Defined once by an admin, bound to many fields.
--   * a LIST INSTANCE is a data holder created from a definition. Two kinds, one table,
--     discriminated by `kind`:
--       - 'resource': the rows belong to one resource, addressed by (resource_id,
--         field_id). Anonymous — the custom field's label names it. Created lazily by
--         the API resolver, never by a read.
--       - 'shared': admin-created and named, referenced by many resources through a
--         lookup field. Editing a row propagates to every resource pointing at it.
--
-- One table rather than two because the rows, the columns, and every read path are
-- identical; only ownership differs, and a CHECK expresses that in four lines where a
-- second table would duplicate list_rows and its whole API surface.
--
-- Two custom-field data types rather than one type with a mode flag, because the value
-- shapes genuinely differ:
--
--   * 'list' (per-resource) stores NO value in resources.custom_fields. Rows are found by
--     (resource_id, field_id), so a whole-document replace of custom_fields on the
--     resource form can never clobber them, and row edits commit independently of the
--     form. Such a field cannot be required — there is no value to require.
--   * 'list_lookup' (shared) stores a JSON array of row ids picked from one shared
--     instance. Required means a non-empty array.
--
-- Because data_type is immutable (1770), modelling them as two types also makes switching
-- modes impossible for free, rather than needing a rule to forbid it.
--
-- 'select' lands here as a list column type only. 1770 deferred it for scalar custom
-- fields ("an options editor is a UI question that has not been asked yet"); that
-- deferral still stands for resource_custom_fields.data_type, which this migration widens
-- only with the two list types.
--
-- Rollback: see the matching revert script (drops the four tables and the two binding
-- columns; list data is not recoverable).

BEGIN;

CREATE TABLE IF NOT EXISTS public.list_definitions (
    id          UUID         NOT NULL DEFAULT gen_random_uuid(),
    -- No key column, unlike resource_custom_fields: a definition is referenced by id from
    -- the binding columns below, never by name from a JSON document. Named like
    -- job_titles instead — unique, human-chosen, renameable.
    name        VARCHAR(100) NOT NULL,
    description TEXT,
    -- Retiring a definition is deactivation, not deletion: delete is RESTRICTed while
    -- anything still binds to it, so this is the way to take one out of circulation.
    is_active   BOOLEAN      NOT NULL DEFAULT true,
    created_at  TIMESTAMPTZ  NOT NULL DEFAULT now(),
    updated_at  TIMESTAMPTZ  NOT NULL DEFAULT now(),

    CONSTRAINT list_definitions_pkey PRIMARY KEY (id),
    CONSTRAINT list_definitions_name_unique UNIQUE (name)
);

CREATE TRIGGER list_definitions_updated_at
    BEFORE UPDATE ON public.list_definitions
    FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();

CREATE TABLE IF NOT EXISTS public.list_columns (
    id                 UUID         NOT NULL DEFAULT gen_random_uuid(),
    list_definition_id UUID         NOT NULL,
    -- Immutable once created, for 1770's reason: it is the key inside every row's values
    -- document, so renaming it would orphan the data already stored under it.
    key                VARCHAR(50)  NOT NULL,
    label              VARCHAR(100) NOT NULL,
    description        TEXT,
    -- Also immutable — changing it would reinterpret values already written under the key.
    -- 'select' exists here and nowhere else (see the header).
    data_type          VARCHAR(20)  NOT NULL
        CONSTRAINT list_columns_data_type_check
            CHECK (data_type IN ('text', 'number', 'boolean', 'date', 'url', 'select')),
    -- Options for 'select', as a JSON array of strings. Editable: removing an option never
    -- rewrites stored rows, matching criteria enum_values semantics (validated on write).
    options            JSONB,
    is_required        BOOLEAN      NOT NULL DEFAULT false,
    -- Unlike rows, columns carry an order: it is the order of the fields in the row form.
    sort_order         INT          NOT NULL DEFAULT 0,
    is_active          BOOLEAN      NOT NULL DEFAULT true,
    created_at         TIMESTAMPTZ  NOT NULL DEFAULT now(),
    updated_at         TIMESTAMPTZ  NOT NULL DEFAULT now(),

    CONSTRAINT list_columns_pkey PRIMARY KEY (id),
    CONSTRAINT list_columns_key_unique UNIQUE (list_definition_id, key),
    CONSTRAINT list_columns_key_format CHECK (key ~ '^[a-z][a-z0-9_]{0,49}$'),
    CONSTRAINT list_columns_options_only_for_select
        CHECK ((data_type = 'select') OR (options IS NULL)),
    CONSTRAINT list_columns_definition_fkey
        FOREIGN KEY (list_definition_id) REFERENCES public.list_definitions(id) ON DELETE CASCADE
);

CREATE TRIGGER list_columns_updated_at
    BEFORE UPDATE ON public.list_columns
    FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();

CREATE TABLE IF NOT EXISTS public.list_instances (
    id                 UUID         NOT NULL DEFAULT gen_random_uuid(),
    list_definition_id UUID         NOT NULL,
    kind               VARCHAR(10)  NOT NULL
        CONSTRAINT list_instances_kind_check CHECK (kind IN ('shared', 'resource')),
    -- Shared instances are named and admin-created; per-resource instances are anonymous
    -- and carry the owning (resource, field) pair instead. The CHECK below makes each kind
    -- carry exactly its own columns, so neither shape can be half-populated.
    name               VARCHAR(100),
    resource_id        UUID,
    field_id           UUID,
    created_at         TIMESTAMPTZ  NOT NULL DEFAULT now(),
    updated_at         TIMESTAMPTZ  NOT NULL DEFAULT now(),

    CONSTRAINT list_instances_pkey PRIMARY KEY (id),
    CONSTRAINT list_instances_kind_shape CHECK (
        (kind = 'shared'   AND name IS NOT NULL AND resource_id IS NULL AND field_id IS NULL)
     OR (kind = 'resource' AND name IS NULL AND resource_id IS NOT NULL AND field_id IS NOT NULL)
    ),
    -- Both UNIQUEs are plain, not partial: NULLs do not collide, so each constraint only
    -- ever binds rows of its own kind. The second one is also the arbiter the resolver's
    -- ON CONFLICT needs, which a partial index would not serve as cleanly.
    CONSTRAINT list_instances_shared_name_unique UNIQUE (list_definition_id, name),
    CONSTRAINT list_instances_resource_field_unique UNIQUE (resource_id, field_id),
    CONSTRAINT list_instances_definition_fkey
        FOREIGN KEY (list_definition_id) REFERENCES public.list_definitions(id) ON DELETE RESTRICT,
    -- Covers the paths that really remove a resource row, such as a tenant purge. Note that
    -- DELETE /api/resources does NOT take this path: it deactivates (is_active = false), so a
    -- deactivated resource keeps its list data, which is the coherent outcome — one restored
    -- without its history would have lost data nobody agreed to discard.
    CONSTRAINT list_instances_resource_fkey
        FOREIGN KEY (resource_id) REFERENCES public.resources(id) ON DELETE CASCADE,
    -- Deleting the field that defines the list does the same — mirroring 1770's rule that
    -- removing a definition removes the values captured under it.
    CONSTRAINT list_instances_field_fkey
        FOREIGN KEY (field_id) REFERENCES public.resource_custom_fields(id) ON DELETE CASCADE
);

CREATE TRIGGER list_instances_updated_at
    BEFORE UPDATE ON public.list_instances
    FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();

CREATE TABLE IF NOT EXISTS public.list_rows (
    id               UUID        NOT NULL DEFAULT gen_random_uuid(),
    list_instance_id UUID        NOT NULL,
    -- Cell values keyed by list_columns.key, same storage decision as 1770: rows are always
    -- read with their instance, never queried across instances, so a jsonb document keeps a
    -- row one row.
    values           JSONB       NOT NULL DEFAULT '{}'::jsonb,
    created_at       TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at       TIMESTAMPTZ NOT NULL DEFAULT now(),

    CONSTRAINT list_rows_pkey PRIMARY KEY (id),
    CONSTRAINT list_rows_instance_fkey
        FOREIGN KEY (list_instance_id) REFERENCES public.list_instances(id) ON DELETE CASCADE
);

CREATE TRIGGER list_rows_updated_at
    BEFORE UPDATE ON public.list_rows
    FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();

COMMENT ON TABLE public.list_definitions IS
    'Reusable list shapes (columns + types) defined by a tenant admin. Never matchable — '
    'the solver never reads list data; use criteria for anything a request can require.';
COMMENT ON TABLE public.list_instances IS
    'A data holder created from a list definition: kind=shared is admin-created and named '
    'and may be referenced by many resources; kind=resource belongs to one (resource, field).';
COMMENT ON TABLE public.list_rows IS
    'Rows of one list instance, values keyed by list_columns.key and validated in the API '
    'against that instance''s definition.';

-- Widen the custom-field data_type CHECK by same-name drop and re-add (the technique 1610
-- used on requests_status_check). The two new types are inert until the API admits them.
ALTER TABLE public.resource_custom_fields
    DROP CONSTRAINT IF EXISTS resource_custom_fields_data_type_check;

ALTER TABLE public.resource_custom_fields
    ADD CONSTRAINT resource_custom_fields_data_type_check
        CHECK (data_type IN ('text', 'number', 'boolean', 'date', 'url', 'list', 'list_lookup'));

-- What a list field points at. 'list' names the shape its per-resource rows take;
-- 'list_lookup' names the one shared instance its values are picked from. Both RESTRICT:
-- a definition or shared instance in use cannot be deleted out from under a field.
ALTER TABLE public.resource_custom_fields
    ADD COLUMN IF NOT EXISTS list_definition_id UUID,
    ADD COLUMN IF NOT EXISTS list_instance_id   UUID;

ALTER TABLE public.resource_custom_fields
    ADD CONSTRAINT resource_custom_fields_list_definition_fkey
        FOREIGN KEY (list_definition_id) REFERENCES public.list_definitions(id) ON DELETE RESTRICT,
    ADD CONSTRAINT resource_custom_fields_list_instance_fkey
        FOREIGN KEY (list_instance_id) REFERENCES public.list_instances(id) ON DELETE RESTRICT;

-- Bidirectional: each list type must carry its own binding, and a scalar field must carry
-- neither. Written as equivalences so a stray binding on a text field is rejected too.
ALTER TABLE public.resource_custom_fields
    ADD CONSTRAINT resource_custom_fields_list_binding_check CHECK (
        ((data_type = 'list')        = (list_definition_id IS NOT NULL))
    AND ((data_type = 'list_lookup') = (list_instance_id   IS NOT NULL))
    );

COMMENT ON COLUMN public.resource_custom_fields.list_definition_id IS
    'For data_type=''list'': the definition each per-resource instance of this field is built from.';
COMMENT ON COLUMN public.resource_custom_fields.list_instance_id IS
    'For data_type=''list_lookup'': the shared instance whose row ids this field''s values pick from.';

COMMIT;

-- No sort_order on rows: nothing renders a row order, and the data table sorts by column.
-- Rows are read as "every row of one instance", which this index serves; created_at gives
-- them a stable default order.
--
-- CONCURRENTLY cannot run inside a transaction. The runner does not wrap scripts, so
-- statements after COMMIT run in autocommit (the 1720 precedent).
CREATE INDEX CONCURRENTLY IF NOT EXISTS idx_list_rows_instance
    ON public.list_rows (list_instance_id);
