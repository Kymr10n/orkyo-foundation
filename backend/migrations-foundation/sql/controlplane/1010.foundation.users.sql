-- Users — global identity at the control-plane scope.
-- Final state folds in: V001 (base), V003 (keycloak columns), V011 (lifecycle),
-- V012 (has_seen_tour). V013 drop of password_hash is reflected by its absence.

-- Column order matches the historical legacy V001+V003+V011+V012 sequence so that
-- prod databases adopted via --adopt-legacy and fresh installs produce a byte-equivalent
-- pg_dump --schema-only.
CREATE TABLE public.users (
    id                       uuid                     DEFAULT gen_random_uuid() NOT NULL,
    email                    character varying(320)   NOT NULL,
    created_at               timestamp with time zone DEFAULT now() NOT NULL,
    updated_at               timestamp with time zone DEFAULT now() NOT NULL,
    last_login_at            timestamp with time zone,
    display_name             character varying(255)   DEFAULT 'User'::character varying NOT NULL,
    status                   character varying(30)    DEFAULT 'pending_verification'::character varying NOT NULL,

    -- Keycloak integration (V003)
    keycloak_id              character varying(255),
    keycloak_metadata        jsonb,

    -- GDPR lifecycle (V011)
    lifecycle_status         character varying(20),
    lifecycle_warning_count  smallint                 DEFAULT 0 NOT NULL,
    lifecycle_last_warned_at timestamp with time zone,
    lifecycle_dormant_since  timestamp with time zone,
    lifecycle_confirm_token  character varying(36),

    -- Onboarding (V012)
    has_seen_tour            boolean                  DEFAULT false NOT NULL,

    CONSTRAINT users_pkey PRIMARY KEY (id),
    CONSTRAINT users_email_key UNIQUE (email),
    CONSTRAINT users_status_check
        CHECK (((status)::text = ANY ((ARRAY['active'::character varying, 'disabled'::character varying, 'pending_verification'::character varying])::text[])))
);

-- The lifecycle-status CHECK was historically added via ALTER TABLE in V011.
-- Postgres serializes inline-vs-ALTER-added CHECKs differently in pg_dump output
-- even though they are semantically identical, so we keep the ALTER form for
-- byte-equivalence with adopted prod databases.
ALTER TABLE public.users
    ADD CONSTRAINT users_lifecycle_status_check
    CHECK (lifecycle_status IS NULL OR lifecycle_status IN ('warned', 'dormant'));

CREATE INDEX idx_users_email
    ON public.users USING btree (email);

CREATE INDEX idx_users_status
    ON public.users USING btree (status);

CREATE UNIQUE INDEX idx_users_keycloak_id
    ON public.users USING btree (keycloak_id)
    WHERE keycloak_id IS NOT NULL;

CREATE UNIQUE INDEX idx_users_lifecycle_confirm_token
    ON public.users USING btree (lifecycle_confirm_token)
    WHERE lifecycle_confirm_token IS NOT NULL;

CREATE INDEX idx_users_lifecycle_status
    ON public.users USING btree (lifecycle_status)
    WHERE lifecycle_status IS NOT NULL;

COMMENT ON COLUMN public.users.keycloak_id        IS 'Keycloak user subject identifier (sub claim)';
COMMENT ON COLUMN public.users.keycloak_metadata  IS 'Additional Keycloak user claims and attributes';
