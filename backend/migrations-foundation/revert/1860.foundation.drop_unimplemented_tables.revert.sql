-- Reverts 1860. Recreates the three tables with their 1210/1230 shapes. The
-- data is gone; only the schema comes back.

BEGIN;

CREATE TABLE public.invites (
    id                    uuid                     DEFAULT gen_random_uuid() NOT NULL,
    email                 character varying(320)   NOT NULL,
    invited_by_user_id    uuid,
    scope                 character varying(20)    NOT NULL,
    site_id               uuid,
    role                  character varying(50)    NOT NULL,
    token_hash            character varying(255)   NOT NULL,
    expires_at            timestamp with time zone NOT NULL,
    accepted_at           timestamp with time zone,
    created_at            timestamp with time zone DEFAULT now() NOT NULL,

    CONSTRAINT invites_pkey PRIMARY KEY (id),
    CONSTRAINT invites_scope_check
        CHECK (((scope)::text = ANY ((ARRAY['tenant'::character varying, 'site'::character varying])::text[]))),
    CONSTRAINT invites_invited_by_user_id_fkey
        FOREIGN KEY (invited_by_user_id) REFERENCES public.users(id) ON DELETE SET NULL,
    CONSTRAINT invites_site_id_fkey
        FOREIGN KEY (site_id) REFERENCES public.sites(id) ON DELETE CASCADE
);

CREATE INDEX idx_invites_email      ON public.invites USING btree (email);
CREATE INDEX idx_invites_expires_at ON public.invites USING btree (expires_at);
CREATE INDEX idx_invites_token_hash ON public.invites USING btree (token_hash);

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

COMMIT;
