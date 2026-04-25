-- Criteria — typed attribute definitions used by space capabilities and request requirements.
-- Folds V007 case-insensitive unique index.

CREATE TABLE public.criteria (
    id          uuid                     DEFAULT gen_random_uuid() NOT NULL,
    name        character varying(100)   NOT NULL,
    description text,
    data_type   character varying(20)    NOT NULL,
    enum_values jsonb,
    unit        character varying(20),
    created_at  timestamp with time zone DEFAULT now() NOT NULL,
    updated_at  timestamp with time zone DEFAULT now() NOT NULL,

    CONSTRAINT criteria_pkey PRIMARY KEY (id),
    CONSTRAINT criteria_name_key UNIQUE (name),
    CONSTRAINT criteria_data_type_check
        CHECK (((data_type)::text = ANY ((ARRAY['Boolean'::character varying, 'Number'::character varying, 'String'::character varying, 'Enum'::character varying])::text[])))
);

CREATE INDEX idx_criteria_name              ON public.criteria USING btree (name);
CREATE UNIQUE INDEX idx_criteria_name_lower ON public.criteria (LOWER(name));
