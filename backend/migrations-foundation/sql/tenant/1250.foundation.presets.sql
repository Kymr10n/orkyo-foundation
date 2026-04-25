-- Preset application history + entity-id mapping. Used to track which catalog preset
-- versions have been applied to this tenant and how their logical keys mapped to
-- newly-created entity ids.

CREATE TABLE public.preset_applications (
    id                  uuid                     DEFAULT gen_random_uuid() NOT NULL,
    preset_id           character varying(100)   NOT NULL,
    preset_version      character varying(20)    NOT NULL,
    applied_at          timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    updated_at          timestamp with time zone,
    applied_by_user_id  uuid,

    CONSTRAINT preset_applications_pkey PRIMARY KEY (id),
    CONSTRAINT uq_preset_applications_preset_id UNIQUE (preset_id),
    CONSTRAINT preset_applications_applied_by_user_id_fkey
        FOREIGN KEY (applied_by_user_id) REFERENCES public.users(id) ON DELETE SET NULL
);

CREATE INDEX idx_preset_applications_preset_id ON public.preset_applications USING btree (preset_id);

CREATE TABLE public.preset_mappings (
    id                    uuid                     DEFAULT gen_random_uuid() NOT NULL,
    preset_application_id uuid                     NOT NULL,
    entity_type           character varying(50)    NOT NULL,
    logical_key           character varying(100)   NOT NULL,
    entity_id             uuid                     NOT NULL,
    created_at            timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,

    CONSTRAINT preset_mappings_pkey PRIMARY KEY (id),
    CONSTRAINT uq_preset_mappings_key UNIQUE (preset_application_id, entity_type, logical_key),
    CONSTRAINT preset_mappings_preset_application_id_fkey
        FOREIGN KEY (preset_application_id) REFERENCES public.preset_applications(id) ON DELETE CASCADE
);

CREATE INDEX idx_preset_mappings_application ON public.preset_mappings USING btree (preset_application_id);
CREATE INDEX idx_preset_mappings_entity      ON public.preset_mappings USING btree (entity_type, entity_id);

-- Default site seed (preserved from legacy V001 tail).
INSERT INTO public.sites (name, code)
SELECT 'Default Site', 'default'
WHERE NOT EXISTS (SELECT 1 FROM public.sites);
