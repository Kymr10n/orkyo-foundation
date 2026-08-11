-- @migration-class: expand

-- Per-type descriptive fields on a resource — a serial number, an asset tag, a link to
-- the datasheet. Tenant-defined, because the interesting ones differ per tenant and no
-- fixed column set survives contact with a second customer.
--
-- This reopens ground that migration 1680 closed, so the boundary has to be stated once
-- and held. 1680 retired resource_type_fields because attributes were modelled twice and
-- the modeller had to guess up front which system a value belonged in. That argument is
-- about *matchable* attributes, and it still stands: criteria remain the only way to
-- describe something a request can require and the solver can reason over.
--
-- These fields are the other kind, and they are defined by what they are not:
--
--   * never matchable. No request requirement points at them, no capability matcher
--     reads them, nothing here reaches the solver. A value that ought to drive an
--     assignment is a criterion, and belongs in the criteria system instead.
--   * descriptive only. They render on the resource form and in exports; that is all
--     they do.
--
-- The split is also who-decides: defining a field is tenant governance (Admin), filling
-- one in is ordinary resource editing (Editor). The API enforces that; the schema only
-- records it.
--
-- Values live in resources.custom_fields, keyed by resource_custom_fields.key, validated
-- in the API against the definitions for that resource type. Deliberately not a value
-- table: values are always read with their resource, never queried across resources, and
-- a jsonb column keeps a resource one row.
--
-- data_type omits 'select' on purpose — an options editor is a UI question that has not
-- been asked yet, and an unused enum type is a migration nobody can change later.
--
-- Rollback: see the matching revert script (drops the table and the column; values are
-- not recoverable).

BEGIN;

CREATE TABLE IF NOT EXISTS public.resource_custom_fields (
    id               UUID         NOT NULL DEFAULT gen_random_uuid(),
    resource_type_id UUID         NOT NULL,
    -- Immutable once created: it is the key inside every resource's custom_fields
    -- document, so renaming it would orphan every stored value.
    key              VARCHAR(50)  NOT NULL,
    label            VARCHAR(100) NOT NULL,
    description      TEXT,
    -- Also immutable — changing it would reinterpret values already written under this key.
    data_type        VARCHAR(20)  NOT NULL
        CONSTRAINT resource_custom_fields_data_type_check
            CHECK (data_type IN ('text', 'number', 'boolean', 'date', 'url')),
    is_required      BOOLEAN      NOT NULL DEFAULT false,
    sort_order       INT          NOT NULL DEFAULT 0,
    -- Deactivating hides a field from the form without discarding values already captured.
    is_active        BOOLEAN      NOT NULL DEFAULT true,
    created_at       TIMESTAMPTZ  NOT NULL DEFAULT now(),
    updated_at       TIMESTAMPTZ  NOT NULL DEFAULT now(),

    CONSTRAINT resource_custom_fields_pkey PRIMARY KEY (id),
    CONSTRAINT resource_custom_fields_key_unique UNIQUE (resource_type_id, key),
    CONSTRAINT resource_custom_fields_key_format CHECK (key ~ '^[a-z][a-z0-9_]{0,49}$'),
    CONSTRAINT resource_custom_fields_type_fkey
        FOREIGN KEY (resource_type_id) REFERENCES public.resource_types(id) ON DELETE CASCADE
);

-- No index beyond the constraints above: definitions are read by primary key, or as "every
-- definition for one type", which resource_custom_fields_key_unique (resource_type_id, key)
-- already serves. The ORDER BY that puts them in form order sorts a handful of rows.

CREATE TRIGGER resource_custom_fields_updated_at
    BEFORE UPDATE ON public.resource_custom_fields
    FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();

COMMENT ON TABLE public.resource_custom_fields IS
    'Tenant-defined descriptive fields per resource type. Never matchable — a request '
    'cannot require one and the solver never reads them; use criteria for that.';

ALTER TABLE public.resources
    ADD COLUMN IF NOT EXISTS custom_fields JSONB NOT NULL DEFAULT '{}'::jsonb;

COMMENT ON COLUMN public.resources.custom_fields IS
    'Values for this resource type''s resource_custom_fields, keyed by field key. '
    'Validated in the API against the active definitions for the type.';

COMMIT;
