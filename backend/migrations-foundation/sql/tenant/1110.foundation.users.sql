-- Tenant-local user mirror — minimal subset of control-plane users, kept in sync
-- by the API layer. Unlike control-plane users, no auth/lifecycle columns live here.
-- Folds in V003 (keycloak columns).

CREATE TABLE public.users (
    id                uuid                     DEFAULT gen_random_uuid() NOT NULL,
    email             character varying(320)   NOT NULL,
    display_name      character varying(255),
    keycloak_id       character varying(255),
    keycloak_metadata jsonb,
    synced_at         timestamp with time zone DEFAULT now(),
    created_at        timestamp with time zone DEFAULT now() NOT NULL,

    CONSTRAINT users_pkey PRIMARY KEY (id),
    CONSTRAINT users_email_key UNIQUE (email)
);

CREATE UNIQUE INDEX idx_users_keycloak_id
    ON public.users USING btree (keycloak_id)
    WHERE keycloak_id IS NOT NULL;

COMMENT ON COLUMN public.users.keycloak_id       IS 'Keycloak user subject identifier (sub claim)';
COMMENT ON COLUMN public.users.keycloak_metadata IS 'Additional Keycloak user claims and attributes';
