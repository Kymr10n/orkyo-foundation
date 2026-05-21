-- Invitations — email-based user onboarding into a tenant.
--
-- Owned by Foundation: used by the shared InvitationService consumed by both
-- SaaS (multi-tenant, discriminated by tenant_id) and Community (single-tenant).
--
-- @migration-class: expand

CREATE TABLE public.invitations (
    id          uuid                     DEFAULT gen_random_uuid() NOT NULL,
    email       character varying(320)   NOT NULL,
    role        character varying(30)    NOT NULL,
    invited_by  uuid                     NOT NULL,
    token_hash  character varying(255)   NOT NULL,
    expires_at  timestamp with time zone NOT NULL,
    accepted_at timestamp with time zone,
    created_at  timestamp with time zone DEFAULT now() NOT NULL,
    updated_at  timestamp with time zone DEFAULT now() NOT NULL,
    tenant_id   uuid,

    CONSTRAINT invitations_pkey            PRIMARY KEY (id),
    CONSTRAINT invitations_token_hash_key  UNIQUE (token_hash),
    CONSTRAINT invitations_role_check
        CHECK (((role)::text = ANY ((ARRAY['admin'::character varying, 'editor'::character varying, 'viewer'::character varying])::text[]))),
    CONSTRAINT invitations_invited_by_fkey
        FOREIGN KEY (invited_by) REFERENCES public.users(id) ON DELETE CASCADE,
    CONSTRAINT invitations_tenant_id_fkey
        FOREIGN KEY (tenant_id) REFERENCES public.tenants(id) ON DELETE CASCADE
);

CREATE INDEX idx_invitations_email      ON public.invitations USING btree (email);
CREATE INDEX idx_invitations_expires_at ON public.invitations USING btree (expires_at);
CREATE INDEX idx_invitations_tenant_id  ON public.invitations USING btree (tenant_id);
CREATE INDEX idx_invitations_token_hash ON public.invitations USING btree (token_hash);

CREATE TRIGGER set_invitations_updated_at
    BEFORE UPDATE ON public.invitations
    FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();
