-- @migration-class: expand
-- Description: Reporting API tokens for external BI tool integration
CREATE TABLE IF NOT EXISTS public.reporting_api_tokens (
    id                   uuid                     DEFAULT gen_random_uuid() NOT NULL,
    tenant_id            uuid                     NOT NULL,
    name                 character varying(255)   NOT NULL,
    token_prefix         character varying(16)    NOT NULL,
    token_hash           character varying(128)   NOT NULL,
    scopes               character varying(255)   NOT NULL DEFAULT 'reporting:read',
    created_at           timestamp with time zone DEFAULT now() NOT NULL,
    created_by_user_id   uuid,
    last_used_at         timestamp with time zone,
    expires_at           timestamp with time zone,
    revoked_at           timestamp with time zone,
    revoked_by_user_id   uuid,

    CONSTRAINT reporting_api_tokens_pkey PRIMARY KEY (id),
    CONSTRAINT reporting_api_tokens_prefix_key UNIQUE (token_prefix),
    CONSTRAINT reporting_api_tokens_tenant_fkey
        FOREIGN KEY (tenant_id) REFERENCES public.tenants(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_reporting_tokens_tenant_id
    ON public.reporting_api_tokens (tenant_id);

CREATE INDEX IF NOT EXISTS idx_reporting_tokens_prefix
    ON public.reporting_api_tokens (token_prefix)
    WHERE revoked_at IS NULL;
