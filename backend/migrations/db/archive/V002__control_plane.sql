-- Control Plane Database Schema
-- This database stores tenant registry and global configuration
-- Applied to the database specified by the migration script

-- Tenants table
CREATE TABLE IF NOT EXISTS tenants (
	id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
	slug VARCHAR(63) NOT NULL UNIQUE,
	display_name VARCHAR(255) NOT NULL,
	status VARCHAR(20) NOT NULL DEFAULT 'active' CHECK (status IN ('active', 'suspended', 'deleting')),
	db_identifier VARCHAR(255) NOT NULL, -- Used to construct tenant DB connection string
	created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
	updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_tenants_slug ON tenants(slug);
CREATE INDEX IF NOT EXISTS idx_tenants_status ON tenants(status);

-- Tenant domain policies (optional for verified domain auto-join)
CREATE TABLE IF NOT EXISTS tenant_domain_policies (
	id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
	tenant_id UUID NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
	verified_domain VARCHAR(255) NOT NULL,
	policy VARCHAR(50) NOT NULL DEFAULT 'invite_only' CHECK (policy IN ('invite_only', 'domain_auto_join')),
	created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
	UNIQUE(tenant_id, verified_domain)
);

-- OAuth provider configurations (tenant-specific or global)
CREATE TABLE IF NOT EXISTS oauth_provider_configs (
	id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
	tenant_id UUID REFERENCES tenants(id) ON DELETE CASCADE, -- NULL for global config
	provider VARCHAR(50) NOT NULL CHECK (provider IN ('google', 'azure', 'github')),
	client_id_ref VARCHAR(255) NOT NULL, -- Reference to secrets manager
	client_secret_ref VARCHAR(255) NOT NULL, -- Reference to secrets manager
	enabled BOOLEAN NOT NULL DEFAULT true,
	created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
	updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
	UNIQUE(tenant_id, provider)
);

-- Update timestamp trigger function
CREATE OR REPLACE FUNCTION update_updated_at_column()
RETURNS TRIGGER AS $$
BEGIN
	NEW.updated_at = NOW();
	RETURN NEW;
END;
$$ language 'plpgsql';


-- Drop triggers if they exist before creating (idempotent)
DO $$ BEGIN
	IF EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'update_tenants_updated_at') THEN
		EXECUTE 'DROP TRIGGER update_tenants_updated_at ON tenants';
	END IF;
END $$;
CREATE TRIGGER update_tenants_updated_at BEFORE UPDATE ON tenants
	FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();

DO $$ BEGIN
	IF EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'update_oauth_provider_configs_updated_at') THEN
		EXECUTE 'DROP TRIGGER update_oauth_provider_configs_updated_at ON oauth_provider_configs';
	END IF;
END $$;
CREATE TRIGGER update_oauth_provider_configs_updated_at BEFORE UPDATE ON oauth_provider_configs
	FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();
