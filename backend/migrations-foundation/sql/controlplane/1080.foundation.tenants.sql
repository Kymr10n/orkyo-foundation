-- Tenants — canonical control-plane tenant identity.
--
-- Owned by foundation: every product (SaaS, Community) inherits this base
-- contract because foundation source code (TenantResolverQueryContract,
-- TenantOwnerStatusQueryContract, etc.) queries these columns directly.
--
-- Product-specific extensions (e.g. SaaS dormancy/billing columns,
-- Community single-tenant seed) are layered on top via ALTER TABLE in
-- product migration modules (Order > 1000).
-- @migration-class: expand
-- Description: Create canonical tenants table for cross-product tenant identity
-- Rollback: DROP TABLE public.tenants;

CREATE TABLE IF NOT EXISTS public.tenants (
    id                uuid                     DEFAULT gen_random_uuid() NOT NULL,
    slug              character varying(63)    NOT NULL,
    display_name      character varying(255)   NOT NULL,
    status            character varying(20)    DEFAULT 'active'::character varying NOT NULL,
    db_identifier     character varying(255)   NOT NULL,
    owner_user_id     uuid,
    tier              integer                  DEFAULT 0 NOT NULL,
    suspension_reason character varying(50),
    created_at        timestamp with time zone DEFAULT now() NOT NULL,
    updated_at        timestamp with time zone DEFAULT now() NOT NULL,

    CONSTRAINT tenants_pkey PRIMARY KEY (id),
    CONSTRAINT tenants_slug_key UNIQUE (slug),
    CONSTRAINT tenants_status_check
        CHECK ((status)::text = ANY ((ARRAY[
            'active'::character varying,
            'suspended'::character varying,
            'deleting'::character varying
        ])::text[])),
    CONSTRAINT tenants_tier_check CHECK (tier >= 0 AND tier <= 2),
    CONSTRAINT tenants_suspension_reason_check
        CHECK (suspension_reason IS NULL OR suspension_reason IN (
            'inactivity', 'policy_violation', 'payment', 'manual', 'gdpr', 'manual_admin'
        )),
    CONSTRAINT tenants_owner_user_id_fkey
        FOREIGN KEY (owner_user_id) REFERENCES public.users(id) ON DELETE SET NULL
);

CREATE INDEX idx_tenants_slug          ON public.tenants USING btree (slug);
CREATE INDEX idx_tenants_status        ON public.tenants USING btree (status);
CREATE INDEX idx_tenants_owner_user_id ON public.tenants USING btree (owner_user_id);
CREATE INDEX idx_tenants_tier          ON public.tenants USING btree (tier);

CREATE TRIGGER update_tenants_updated_at
    BEFORE UPDATE ON public.tenants
    FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();
