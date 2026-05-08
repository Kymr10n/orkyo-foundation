-- Tenant memberships — canonical user ↔ tenant relationship.
--
-- Owned by foundation: queried by TenantMembershipRoleStatusQueryContract,
-- TenantLeaveLookupQueryContract, etc.
--
-- Composite primary key (user_id, tenant_id) enforces uniqueness directly.
-- Product-specific extensions (e.g. SaaS invited_by tracking) are added via
-- ALTER TABLE in product migration modules.
--
-- @migration-class: expand
-- Description: Create canonical tenant_memberships table for user-tenant role associations
-- Rollback: DROP TABLE public.tenant_memberships;

CREATE TABLE public.tenant_memberships (
    user_id    uuid                     NOT NULL,
    tenant_id  uuid                     NOT NULL,
    role       character varying(30)    DEFAULT 'viewer'::character varying NOT NULL,
    status     character varying(30)    DEFAULT 'active'::character varying NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,

    CONSTRAINT tenant_memberships_pkey PRIMARY KEY (user_id, tenant_id),
    CONSTRAINT tenant_memberships_role_check
        CHECK ((role)::text = ANY ((ARRAY[
            'admin'::character varying,
            'editor'::character varying,
            'viewer'::character varying
        ])::text[])),
    CONSTRAINT tenant_memberships_status_check
        CHECK ((status)::text = ANY ((ARRAY[
            'active'::character varying,
            'pending'::character varying,
            'disabled'::character varying
        ])::text[])),
    CONSTRAINT tenant_memberships_user_id_fkey
        FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE,
    CONSTRAINT tenant_memberships_tenant_id_fkey
        FOREIGN KEY (tenant_id) REFERENCES public.tenants(id) ON DELETE CASCADE
);

CREATE INDEX idx_tenant_memberships_user_id   ON public.tenant_memberships USING btree (user_id);
CREATE INDEX idx_tenant_memberships_tenant_id ON public.tenant_memberships USING btree (tenant_id);
CREATE INDEX idx_tenant_memberships_role      ON public.tenant_memberships USING btree (role);

CREATE TRIGGER update_tenant_memberships_updated_at
    BEFORE UPDATE ON public.tenant_memberships
    FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();
