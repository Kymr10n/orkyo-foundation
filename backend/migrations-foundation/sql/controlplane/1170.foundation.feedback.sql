-- Platform feedback relocated to the control-plane so a site-admin can triage every submission in
-- one place (mirrors announcements, which is also control-plane). Adds tenant_id to record which
-- tenant a report came from. The old per-tenant `feedback` table (tenant migration 1240) is left
-- in place; new writes go here.
--
-- @migration-class: expand

CREATE TABLE public.feedback (
    id               uuid                     DEFAULT gen_random_uuid() NOT NULL,
    tenant_id        uuid                     NOT NULL,
    user_id          uuid,
    feedback_type    character varying(20)    NOT NULL,
    title            character varying(200)   NOT NULL,
    description      text,
    page_url         text,
    user_agent       text,
    status           character varying(20)    DEFAULT 'new'::character varying NOT NULL,
    admin_notes      text,
    github_issue_url text,
    created_at       timestamp with time zone DEFAULT now() NOT NULL,
    updated_at       timestamp with time zone DEFAULT now() NOT NULL,

    CONSTRAINT feedback_pkey PRIMARY KEY (id),
    CONSTRAINT feedback_tenant_id_fkey
        FOREIGN KEY (tenant_id) REFERENCES public.tenants(id) ON DELETE CASCADE,
    CONSTRAINT feedback_user_id_fkey
        FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE SET NULL,
    CONSTRAINT feedback_feedback_type_check
        CHECK (((feedback_type)::text = ANY ((ARRAY['bug'::character varying, 'feature'::character varying, 'question'::character varying, 'other'::character varying])::text[]))),
    CONSTRAINT feedback_status_check
        CHECK (((status)::text = ANY ((ARRAY['new'::character varying, 'reviewed'::character varying, 'resolved'::character varying, 'wont_fix'::character varying])::text[])))
);

CREATE INDEX idx_feedback_status   ON public.feedback USING btree (status);
CREATE INDEX idx_feedback_created  ON public.feedback USING btree (created_at DESC);
CREATE INDEX idx_feedback_tenant   ON public.feedback (tenant_id);
CREATE INDEX idx_feedback_user_id  ON public.feedback (user_id);
