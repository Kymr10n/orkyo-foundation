CREATE OR REPLACE FUNCTION public.update_updated_at_column() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
	NEW.updated_at = NOW();
	RETURN NEW;
END;
$$;
CREATE TABLE public.audit_events (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    actor_user_id uuid,
    actor_type character varying(20) DEFAULT 'user'::character varying NOT NULL,
    action character varying(100) NOT NULL,
    target_type character varying(50),
    target_id character varying(255),
    metadata jsonb DEFAULT '{}'::jsonb,
    request_id character varying(100),
    ip_address character varying(45),
    user_agent text,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT audit_events_actor_type_check CHECK (((actor_type)::text = ANY ((ARRAY['user'::character varying, 'system'::character varying, 'api'::character varying])::text[])))
);
CREATE TABLE public.invitations (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    email character varying(320) NOT NULL,
    role character varying(30) NOT NULL,
    invited_by uuid NOT NULL,
    token_hash character varying(255) NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    accepted_at timestamp with time zone,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    tenant_id uuid,
    CONSTRAINT invitations_role_check CHECK (((role)::text = ANY ((ARRAY['admin'::character varying, 'editor'::character varying, 'viewer'::character varying])::text[])))
);
CREATE TABLE public.oauth_provider_configs (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    tenant_id uuid,
    provider character varying(50) NOT NULL,
    client_id_ref character varying(255) NOT NULL,
    client_secret_ref character varying(255) NOT NULL,
    enabled boolean DEFAULT true NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT oauth_provider_configs_provider_check CHECK (((provider)::text = ANY ((ARRAY['google'::character varying, 'azure'::character varying, 'github'::character varying])::text[])))
);
CREATE TABLE public.tenant_domain_policies (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    tenant_id uuid NOT NULL,
    verified_domain character varying(255) NOT NULL,
    policy character varying(50) DEFAULT 'invite_only'::character varying NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT tenant_domain_policies_policy_check CHECK (((policy)::text = ANY ((ARRAY['invite_only'::character varying, 'domain_auto_join'::character varying])::text[])))
);
CREATE TABLE public.tenant_memberships (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    user_id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    role character varying(30) DEFAULT 'viewer'::character varying NOT NULL,
    status character varying(30) DEFAULT 'active'::character varying NOT NULL,
    invited_by uuid,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT tenant_memberships_role_check CHECK (((role)::text = ANY ((ARRAY['admin'::character varying, 'editor'::character varying, 'viewer'::character varying])::text[]))),
    CONSTRAINT tenant_memberships_status_check CHECK (((status)::text = ANY ((ARRAY['active'::character varying, 'pending'::character varying, 'disabled'::character varying])::text[])))
);
CREATE TABLE public.tenants (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    slug character varying(63) NOT NULL,
    display_name character varying(255) NOT NULL,
    status character varying(20) DEFAULT 'active'::character varying NOT NULL,
    db_identifier character varying(255) NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT tenants_status_check CHECK (((status)::text = ANY ((ARRAY['active'::character varying, 'suspended'::character varying, 'deleting'::character varying])::text[])))
);
CREATE TABLE public.user_credentials (
    user_id uuid NOT NULL,
    password_hash character varying(255) NOT NULL,
    email_verified boolean DEFAULT false NOT NULL,
    email_verification_token_hash character varying(255),
    email_verification_expires_at timestamp with time zone,
    password_reset_token_hash character varying(255),
    password_reset_expires_at timestamp with time zone,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL
);
CREATE TABLE public.user_identities (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    user_id uuid NOT NULL,
    provider character varying(50) NOT NULL,
    provider_subject character varying(255) NOT NULL,
    provider_email character varying(320),
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT user_identities_provider_check CHECK (((provider)::text = ANY ((ARRAY['google'::character varying, 'azure'::character varying, 'github'::character varying])::text[])))
);
CREATE TABLE public.users (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    email character varying(320) NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    last_login_at timestamp with time zone,
    display_name character varying(255) DEFAULT 'User'::character varying NOT NULL,
    status character varying(30) DEFAULT 'pending_verification'::character varying NOT NULL,
    password_hash character varying(255),
    CONSTRAINT users_status_check CHECK (((status)::text = ANY ((ARRAY['active'::character varying, 'disabled'::character varying, 'pending_verification'::character varying])::text[])))
);
ALTER TABLE ONLY public.audit_events
    ADD CONSTRAINT audit_events_pkey PRIMARY KEY (id);
ALTER TABLE ONLY public.invitations
    ADD CONSTRAINT invitations_pkey PRIMARY KEY (id);
ALTER TABLE ONLY public.invitations
    ADD CONSTRAINT invitations_token_hash_key UNIQUE (token_hash);
ALTER TABLE ONLY public.oauth_provider_configs
    ADD CONSTRAINT oauth_provider_configs_pkey PRIMARY KEY (id);
ALTER TABLE ONLY public.oauth_provider_configs
    ADD CONSTRAINT oauth_provider_configs_tenant_id_provider_key UNIQUE (tenant_id, provider);
ALTER TABLE ONLY public.tenant_domain_policies
    ADD CONSTRAINT tenant_domain_policies_pkey PRIMARY KEY (id);
ALTER TABLE ONLY public.tenant_domain_policies
    ADD CONSTRAINT tenant_domain_policies_tenant_id_verified_domain_key UNIQUE (tenant_id, verified_domain);
ALTER TABLE ONLY public.tenant_memberships
    ADD CONSTRAINT tenant_memberships_pkey PRIMARY KEY (id);
ALTER TABLE ONLY public.tenant_memberships
    ADD CONSTRAINT tenant_memberships_user_id_tenant_id_key UNIQUE (user_id, tenant_id);
ALTER TABLE ONLY public.tenants
    ADD CONSTRAINT tenants_pkey PRIMARY KEY (id);
ALTER TABLE ONLY public.tenants
    ADD CONSTRAINT tenants_slug_key UNIQUE (slug);
ALTER TABLE ONLY public.user_credentials
    ADD CONSTRAINT user_credentials_pkey PRIMARY KEY (user_id);
ALTER TABLE ONLY public.user_identities
    ADD CONSTRAINT user_identities_pkey PRIMARY KEY (id);
ALTER TABLE ONLY public.user_identities
    ADD CONSTRAINT user_identities_provider_provider_subject_key UNIQUE (provider, provider_subject);
ALTER TABLE ONLY public.users
    ADD CONSTRAINT users_email_key UNIQUE (email);
ALTER TABLE ONLY public.users
    ADD CONSTRAINT users_pkey PRIMARY KEY (id);
CREATE INDEX idx_audit_events_action ON public.audit_events USING btree (action);
CREATE INDEX idx_audit_events_actor_user_id ON public.audit_events USING btree (actor_user_id);
CREATE INDEX idx_audit_events_created_at ON public.audit_events USING btree (created_at);
CREATE INDEX idx_audit_events_request_id ON public.audit_events USING btree (request_id) WHERE (request_id IS NOT NULL);
CREATE INDEX idx_audit_events_target_type ON public.audit_events USING btree (target_type);
CREATE INDEX idx_invitations_email ON public.invitations USING btree (email);
CREATE INDEX idx_invitations_expires_at ON public.invitations USING btree (expires_at);
CREATE INDEX idx_invitations_tenant_id ON public.invitations USING btree (tenant_id);
CREATE INDEX idx_invitations_token_hash ON public.invitations USING btree (token_hash);
CREATE INDEX idx_tenant_memberships_role ON public.tenant_memberships USING btree (role);
CREATE INDEX idx_tenant_memberships_tenant_id ON public.tenant_memberships USING btree (tenant_id);
CREATE INDEX idx_tenant_memberships_user_id ON public.tenant_memberships USING btree (user_id);
CREATE INDEX idx_tenants_slug ON public.tenants USING btree (slug);
CREATE INDEX idx_tenants_status ON public.tenants USING btree (status);
CREATE INDEX idx_user_identities_provider ON public.user_identities USING btree (provider, provider_subject);
CREATE INDEX idx_user_identities_user_id ON public.user_identities USING btree (user_id);
CREATE INDEX idx_users_email ON public.users USING btree (email);
CREATE INDEX idx_users_status ON public.users USING btree (status);
CREATE TRIGGER update_oauth_provider_configs_updated_at BEFORE UPDATE ON public.oauth_provider_configs FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();
CREATE TRIGGER update_tenants_updated_at BEFORE UPDATE ON public.tenants FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();
ALTER TABLE ONLY public.audit_events
    ADD CONSTRAINT audit_events_actor_user_id_fkey FOREIGN KEY (actor_user_id) REFERENCES public.users(id) ON DELETE SET NULL;
ALTER TABLE ONLY public.invitations
    ADD CONSTRAINT invitations_invited_by_fkey FOREIGN KEY (invited_by) REFERENCES public.users(id) ON DELETE CASCADE;
ALTER TABLE ONLY public.invitations
    ADD CONSTRAINT invitations_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES public.tenants(id) ON DELETE CASCADE;
ALTER TABLE ONLY public.oauth_provider_configs
    ADD CONSTRAINT oauth_provider_configs_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES public.tenants(id) ON DELETE CASCADE;
ALTER TABLE ONLY public.tenant_domain_policies
    ADD CONSTRAINT tenant_domain_policies_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES public.tenants(id) ON DELETE CASCADE;
ALTER TABLE ONLY public.tenant_memberships
    ADD CONSTRAINT tenant_memberships_invited_by_fkey FOREIGN KEY (invited_by) REFERENCES public.users(id) ON DELETE SET NULL;
ALTER TABLE ONLY public.tenant_memberships
    ADD CONSTRAINT tenant_memberships_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES public.tenants(id) ON DELETE CASCADE;
ALTER TABLE ONLY public.tenant_memberships
    ADD CONSTRAINT tenant_memberships_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;
ALTER TABLE ONLY public.user_credentials
    ADD CONSTRAINT user_credentials_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;
ALTER TABLE ONLY public.user_identities
    ADD CONSTRAINT user_identities_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;
