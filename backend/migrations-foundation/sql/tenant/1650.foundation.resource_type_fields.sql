-- @migration-class: expand
-- Custom field definitions per resource type. Field values live in
-- resources.metadata_json (added in 1300, unused until now) and are validated
-- in the API layer against these rows.

CREATE TABLE public.resource_type_fields (
    id               UUID         NOT NULL DEFAULT gen_random_uuid(),
    resource_type_id UUID         NOT NULL,
    key              VARCHAR(50)  NOT NULL,
    label            VARCHAR(100) NOT NULL,
    description      TEXT,
    data_type        VARCHAR(20)  NOT NULL
        CONSTRAINT resource_type_fields_data_type_check
            CHECK (data_type IN ('text','number','boolean','date','select')),
    options_json     JSONB,
    validation_json  JSONB,
    is_required      BOOLEAN      NOT NULL DEFAULT false,
    sort_order       INT          NOT NULL DEFAULT 0,
    is_active        BOOLEAN      NOT NULL DEFAULT true,
    created_at       TIMESTAMPTZ  NOT NULL DEFAULT now(),
    updated_at       TIMESTAMPTZ  NOT NULL DEFAULT now(),

    CONSTRAINT resource_type_fields_pkey        PRIMARY KEY (id),
    CONSTRAINT resource_type_fields_key_unique  UNIQUE (resource_type_id, key),
    CONSTRAINT resource_type_fields_key_format  CHECK (key ~ '^[a-z][a-z0-9_]{0,49}$'),
    CONSTRAINT resource_type_fields_type_fkey
        FOREIGN KEY (resource_type_id) REFERENCES public.resource_types(id) ON DELETE CASCADE
);

CREATE INDEX CONCURRENTLY idx_rtf_type ON public.resource_type_fields (resource_type_id, sort_order);

CREATE TRIGGER resource_type_fields_updated_at
    BEFORE UPDATE ON public.resource_type_fields
    FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();
