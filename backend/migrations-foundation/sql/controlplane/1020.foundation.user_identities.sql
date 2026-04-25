-- User identities — links external OAuth/OIDC subjects to internal users.
-- Final state folds in V001 (base) + V004 (adds 'keycloak' to provider check + index).

CREATE TABLE public.user_identities (
    id                uuid                     DEFAULT gen_random_uuid() NOT NULL,
    user_id           uuid                     NOT NULL,
    provider          character varying(50)    NOT NULL,
    provider_subject  character varying(255)   NOT NULL,
    provider_email    character varying(320),
    created_at        timestamp with time zone DEFAULT now() NOT NULL,

    CONSTRAINT user_identities_pkey PRIMARY KEY (id),
    CONSTRAINT user_identities_provider_provider_subject_key UNIQUE (provider, provider_subject),
    CONSTRAINT user_identities_provider_check
        CHECK (provider IN ('google', 'azure', 'github', 'keycloak')),
    CONSTRAINT user_identities_user_id_fkey
        FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE
);

CREATE INDEX idx_user_identities_user_id
    ON public.user_identities USING btree (user_id);

CREATE INDEX idx_user_identities_provider
    ON public.user_identities USING btree (provider, provider_subject);

CREATE INDEX idx_user_identities_keycloak_subject
    ON public.user_identities USING btree (provider_subject)
    WHERE provider = 'keycloak';

COMMENT ON TABLE public.user_identities IS
    'Links external identity provider subjects (OAuth/OIDC) to internal users. Supports Google, Azure, GitHub, and Keycloak providers.';
