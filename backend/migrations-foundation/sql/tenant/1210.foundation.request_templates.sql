-- User-owned request templates + their criterion requirements + per-request requirements.

CREATE TABLE public.request_templates (
    id                     uuid                     DEFAULT gen_random_uuid() NOT NULL,
    user_id                uuid                     NOT NULL,
    name                   character varying(200)   NOT NULL,
    description            text,
    minimal_duration_value integer                  NOT NULL,
    minimal_duration_unit  character varying(20)    NOT NULL,
    created_at             timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    updated_at             timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,

    CONSTRAINT request_templates_pkey PRIMARY KEY (id),
    CONSTRAINT request_templates_user_id_fkey
        FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE,
    CONSTRAINT request_templates_minimal_duration_unit_check
        CHECK (((minimal_duration_unit)::text = ANY ((ARRAY['minutes'::character varying, 'hours'::character varying, 'days'::character varying, 'weeks'::character varying, 'months'::character varying, 'years'::character varying])::text[]))),
    CONSTRAINT request_templates_minimal_duration_value_check
        CHECK ((minimal_duration_value > 0))
);

CREATE INDEX idx_request_templates_user_id            ON public.request_templates USING btree (user_id);
CREATE UNIQUE INDEX idx_request_templates_user_name   ON public.request_templates USING btree (user_id, name);

CREATE TRIGGER update_request_templates_updated_at
    BEFORE UPDATE ON public.request_templates
    FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();

CREATE TABLE public.request_template_requirements (
    id           uuid                     DEFAULT gen_random_uuid() NOT NULL,
    template_id  uuid                     NOT NULL,
    criterion_id uuid                     NOT NULL,
    value        jsonb                    NOT NULL,
    created_at   timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,

    CONSTRAINT request_template_requirements_pkey PRIMARY KEY (id),
    CONSTRAINT request_template_requirements_template_id_criterion_id_key
        UNIQUE (template_id, criterion_id),
    CONSTRAINT request_template_requirements_template_id_fkey
        FOREIGN KEY (template_id)  REFERENCES public.request_templates(id) ON DELETE CASCADE,
    CONSTRAINT request_template_requirements_criterion_id_fkey
        FOREIGN KEY (criterion_id) REFERENCES public.criteria(id)          ON DELETE CASCADE
);

CREATE INDEX idx_template_requirements_template_id  ON public.request_template_requirements USING btree (template_id);
CREATE INDEX idx_template_requirements_criterion_id ON public.request_template_requirements USING btree (criterion_id);

CREATE TABLE public.request_requirements (
    id           uuid                     DEFAULT gen_random_uuid() NOT NULL,
    request_id   uuid                     NOT NULL,
    criterion_id uuid                     NOT NULL,
    value        jsonb                    NOT NULL,
    created_at   timestamp with time zone DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT request_requirements_pkey PRIMARY KEY (id),
    CONSTRAINT request_requirements_request_id_criterion_id_key
        UNIQUE (request_id, criterion_id),
    CONSTRAINT request_requirements_request_id_fkey
        FOREIGN KEY (request_id)   REFERENCES public.requests(id)  ON DELETE CASCADE,
    CONSTRAINT request_requirements_criterion_id_fkey
        FOREIGN KEY (criterion_id) REFERENCES public.criteria(id)  ON DELETE CASCADE
);

CREATE INDEX idx_request_requirements_request_id      ON public.request_requirements USING btree (request_id);
CREATE INDEX idx_request_requirements_criterion_id    ON public.request_requirements USING btree (criterion_id);
CREATE INDEX idx_request_requirements_criterion_value ON public.request_requirements USING btree (criterion_id, value);
