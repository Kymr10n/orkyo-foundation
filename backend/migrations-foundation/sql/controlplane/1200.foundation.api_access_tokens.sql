-- @migration-class: expand
-- Description: Write-capable per-tenant API access tokens (MCP server and future integrations)
--
-- Deliberately a separate table from reporting_api_tokens rather than a scope on it: the two are
-- different trust classes. Reporting tokens are audited and revoked as read-only; these can change
-- the schedule. Keeping them apart means "can this credential write?" is answerable from the table
-- it lives in, without parsing a scopes column.
CREATE TABLE IF NOT EXISTS public.api_access_tokens (
    id                   uuid                     DEFAULT gen_random_uuid() NOT NULL,
    tenant_id            uuid                     NOT NULL,
    name                 character varying(255)   NOT NULL,
    token_prefix         character varying(16)    NOT NULL,
    token_hash           character varying(128)   NOT NULL,
    -- Space-delimited, as OAuth does. No default: every token states its scopes explicitly.
    scopes               text                     NOT NULL,
    created_at           timestamp with time zone DEFAULT now() NOT NULL,
    created_by_user_id   uuid,
    last_used_at         timestamp with time zone,
    expires_at           timestamp with time zone,
    revoked_at           timestamp with time zone,
    revoked_by_user_id   uuid,

    CONSTRAINT api_access_tokens_pkey PRIMARY KEY (id),
    CONSTRAINT api_access_tokens_prefix_key UNIQUE (token_prefix),
    CONSTRAINT api_access_tokens_tenant_fkey
        FOREIGN KEY (tenant_id) REFERENCES public.tenants(id) ON DELETE CASCADE
);

CREATE INDEX CONCURRENTLY IF NOT EXISTS idx_api_access_tokens_tenant_id
    ON public.api_access_tokens (tenant_id);

-- No separate prefix index: api_access_tokens_prefix_key (UNIQUE) already indexes
-- token_prefix, and the validation lookup does not filter on revoked_at.
