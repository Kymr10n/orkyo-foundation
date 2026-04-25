-- User feedback — tenant-scoped reports/suggestions. Folds V006 user_id index.

CREATE TABLE public.feedback (
    id               uuid                     DEFAULT gen_random_uuid() NOT NULL,
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
    CONSTRAINT feedback_user_id_fkey
        FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE SET NULL,
    CONSTRAINT feedback_feedback_type_check
        CHECK (((feedback_type)::text = ANY ((ARRAY['bug'::character varying, 'feature'::character varying, 'question'::character varying, 'other'::character varying])::text[]))),
    CONSTRAINT feedback_status_check
        CHECK (((status)::text = ANY ((ARRAY['new'::character varying, 'reviewed'::character varying, 'resolved'::character varying, 'wont_fix'::character varying])::text[])))
);

CREATE INDEX idx_feedback_status  ON public.feedback USING btree (status);
CREATE INDEX idx_feedback_created ON public.feedback USING btree (created_at DESC);
CREATE INDEX idx_feedback_user_id ON public.feedback (user_id);
