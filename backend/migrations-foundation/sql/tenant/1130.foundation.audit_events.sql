-- Tenant-scope audit events. Folds V006 idx_audit_events_target.

CREATE TABLE IF NOT EXISTS public.audit_events (
    id             uuid                     DEFAULT gen_random_uuid() NOT NULL,
    actor_user_id  uuid,
    actor_type     character varying(20)    DEFAULT 'user'::character varying NOT NULL,
    action         character varying(100)   NOT NULL,
    target_type    character varying(50),
    target_id      character varying(255),
    metadata       jsonb                    DEFAULT '{}'::jsonb,
    request_id     character varying(100),
    ip_address     character varying(45),
    user_agent     text,
    created_at     timestamp with time zone DEFAULT now() NOT NULL,

    CONSTRAINT audit_events_pkey PRIMARY KEY (id),
    CONSTRAINT audit_events_actor_type_check
        CHECK (((actor_type)::text = ANY ((ARRAY['user'::character varying, 'system'::character varying, 'api'::character varying])::text[]))),
    CONSTRAINT audit_events_actor_user_id_fkey
        FOREIGN KEY (actor_user_id) REFERENCES public.users(id) ON DELETE SET NULL
);

CREATE INDEX IF NOT EXISTS idx_audit_events_action        ON public.audit_events USING btree (action);
CREATE INDEX IF NOT EXISTS idx_audit_events_actor_user_id ON public.audit_events USING btree (actor_user_id);
CREATE INDEX IF NOT EXISTS idx_audit_events_created_at    ON public.audit_events USING btree (created_at);
CREATE INDEX IF NOT EXISTS idx_audit_events_target_type   ON public.audit_events USING btree (target_type);
CREATE INDEX IF NOT EXISTS idx_audit_events_request_id    ON public.audit_events USING btree (request_id) WHERE (request_id IS NOT NULL);
CREATE INDEX IF NOT EXISTS idx_audit_events_target        ON public.audit_events (target_type, target_id);
