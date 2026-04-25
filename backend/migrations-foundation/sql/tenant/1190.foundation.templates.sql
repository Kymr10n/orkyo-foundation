-- Templates — typed catalog templates for requests, spaces, and groups.

CREATE TABLE public.templates (
    id             uuid                     DEFAULT gen_random_uuid() NOT NULL,
    name           character varying(255)   NOT NULL,
    description    text,
    entity_type    character varying(20)    NOT NULL,
    duration_value integer,
    duration_unit  character varying(20),
    fixed_start    boolean                  DEFAULT false,
    fixed_end      boolean                  DEFAULT false,
    fixed_duration boolean                  DEFAULT true,
    created_at     timestamp with time zone DEFAULT now() NOT NULL,
    updated_at     timestamp with time zone DEFAULT now() NOT NULL,

    CONSTRAINT templates_pkey PRIMARY KEY (id),
    CONSTRAINT templates_entity_type_check
        CHECK (((entity_type)::text = ANY ((ARRAY['request'::character varying, 'space'::character varying, 'group'::character varying])::text[]))),
    CONSTRAINT templates_duration_unit_check
        CHECK (((duration_unit IS NULL) OR ((duration_unit)::text = ANY ((ARRAY['minutes'::character varying, 'hours'::character varying, 'days'::character varying, 'weeks'::character varying])::text[])))),
    CONSTRAINT templates_duration_value_check
        CHECK (((duration_value IS NULL) OR (duration_value > 0)))
);

CREATE INDEX idx_templates_entity_type ON public.templates USING btree (entity_type);
CREATE INDEX idx_templates_name        ON public.templates USING btree (name);

CREATE TRIGGER update_templates_updated_at
    BEFORE UPDATE ON public.templates
    FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();

CREATE TABLE public.template_items (
    id           uuid                     DEFAULT gen_random_uuid() NOT NULL,
    template_id  uuid                     NOT NULL,
    criterion_id uuid                     NOT NULL,
    value        jsonb                    NOT NULL,
    created_at   timestamp with time zone DEFAULT now() NOT NULL,
    updated_at   timestamp with time zone DEFAULT now() NOT NULL,

    CONSTRAINT template_items_pkey PRIMARY KEY (id),
    CONSTRAINT unique_template_criterion UNIQUE (template_id, criterion_id),
    CONSTRAINT template_items_template_id_fkey
        FOREIGN KEY (template_id) REFERENCES public.templates(id) ON DELETE CASCADE,
    CONSTRAINT template_items_criterion_id_fkey
        FOREIGN KEY (criterion_id) REFERENCES public.criteria(id) ON DELETE CASCADE
);

CREATE INDEX idx_template_items_template_id  ON public.template_items USING btree (template_id);
CREATE INDEX idx_template_items_criterion_id ON public.template_items USING btree (criterion_id);

CREATE TRIGGER update_template_items_updated_at
    BEFORE UPDATE ON public.template_items
    FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();
