
-- FULL INITIAL SCHEMA: All tables, UUID PKs, all columns, idempotent
-- Applied to the database specified by the migration script

-- USERS TABLE (minimal for FKs)
CREATE TABLE IF NOT EXISTS users (
	id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
	email VARCHAR(320) NOT NULL UNIQUE,
	created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- SITES TABLE (UUID PK, all columns)
CREATE TABLE IF NOT EXISTS sites (
	id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
	name VARCHAR(255) NOT NULL,
	code VARCHAR(63) UNIQUE,
	description TEXT,
	address TEXT,
	attributes JSONB,
	floorplan_image_path VARCHAR(500),
	floorplan_mime_type VARCHAR(100),
	floorplan_file_size_bytes BIGINT,
	floorplan_width_px INTEGER,
	floorplan_height_px INTEGER,
	floorplan_uploaded_at TIMESTAMPTZ,
	floorplan_uploaded_by_user_id UUID REFERENCES users(id) ON DELETE SET NULL,
	created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
	updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_sites_code ON sites(code);
CREATE INDEX IF NOT EXISTS idx_sites_floorplan_path ON sites(floorplan_image_path) WHERE floorplan_image_path IS NOT NULL;

-- SPACES TABLE (UUID PK, all columns)
CREATE TABLE IF NOT EXISTS spaces (
	id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
	site_id UUID NOT NULL REFERENCES sites(id) ON DELETE CASCADE,
	code VARCHAR(63),
	name VARCHAR(255) NOT NULL,
	description TEXT,
	is_physical BOOLEAN NOT NULL DEFAULT false,
	geometry JSONB,
	properties JSONB DEFAULT '{}',
	created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
	updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
	CONSTRAINT check_physical_has_geometry CHECK (
		(is_physical = true AND geometry IS NOT NULL) OR (is_physical = false)
	)
);
CREATE INDEX IF NOT EXISTS idx_spaces_site_id ON spaces(site_id);

-- CRITERIA TABLE
CREATE TABLE IF NOT EXISTS criteria (
	id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
	name VARCHAR(100) NOT NULL UNIQUE,
	description TEXT,
	data_type VARCHAR(20) NOT NULL CHECK (data_type IN ('Boolean', 'Number', 'String', 'Enum')),
	enum_values JSONB,
	unit VARCHAR(20),
	created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
	updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_criteria_name ON criteria(name);

-- SPACE CAPABILITIES TABLE
CREATE TABLE IF NOT EXISTS space_capabilities (
	id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
	space_id UUID NOT NULL REFERENCES spaces(id) ON DELETE CASCADE,
	criterion_id UUID NOT NULL REFERENCES criteria(id) ON DELETE CASCADE,
	value JSONB NOT NULL,
	created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
	updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
	CONSTRAINT unique_space_criterion UNIQUE (space_id, criterion_id)
);
CREATE INDEX IF NOT EXISTS idx_space_capabilities_space_id ON space_capabilities(space_id);
CREATE INDEX IF NOT EXISTS idx_space_capabilities_criterion_id ON space_capabilities(criterion_id);

-- MEMBERSHIPS TABLE
CREATE TABLE IF NOT EXISTS memberships (
	id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
	user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
	site_id UUID NOT NULL REFERENCES sites(id) ON DELETE CASCADE,
	role VARCHAR(50) NOT NULL DEFAULT 'read_only' CHECK (role IN ('read_only', 'writer', 'site_admin')),
	created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
	UNIQUE(user_id, site_id)
);
CREATE INDEX IF NOT EXISTS idx_memberships_user_id ON memberships(user_id);
CREATE INDEX IF NOT EXISTS idx_memberships_site_id ON memberships(site_id);

-- INVITES TABLE
CREATE TABLE IF NOT EXISTS invites (
	id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
	email VARCHAR(320) NOT NULL,
	invited_by_user_id UUID REFERENCES users(id) ON DELETE SET NULL,
	scope VARCHAR(20) NOT NULL CHECK (scope IN ('tenant', 'site')),
	site_id UUID REFERENCES sites(id) ON DELETE CASCADE,
	role VARCHAR(50) NOT NULL,
	token_hash VARCHAR(255) NOT NULL,
	expires_at TIMESTAMPTZ NOT NULL,
	accepted_at TIMESTAMPTZ,
	created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_invites_email ON invites(email);
CREATE INDEX IF NOT EXISTS idx_invites_token_hash ON invites(token_hash);
CREATE INDEX IF NOT EXISTS idx_invites_expires_at ON invites(expires_at);

-- TRIGGERS (update_updated_at_column)
CREATE OR REPLACE FUNCTION update_updated_at_column()
RETURNS TRIGGER AS $$
BEGIN
	NEW.updated_at = NOW();
	RETURN NEW;
END;
$$ language 'plpgsql';

DO $$ BEGIN
	IF EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'update_sites_updated_at') THEN
		EXECUTE 'DROP TRIGGER update_sites_updated_at ON sites';
	END IF;
END $$;
CREATE TRIGGER update_sites_updated_at BEFORE UPDATE ON sites
	FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();

DO $$ BEGIN
	IF EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'update_spaces_updated_at') THEN
		EXECUTE 'DROP TRIGGER update_spaces_updated_at ON spaces';
	END IF;
END $$;
CREATE TRIGGER update_spaces_updated_at BEFORE UPDATE ON spaces
	FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();

DO $$ BEGIN
	IF EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'update_space_capabilities_updated_at') THEN
		EXECUTE 'DROP TRIGGER update_space_capabilities_updated_at ON space_capabilities';
	END IF;
END $$;
CREATE TRIGGER update_space_capabilities_updated_at BEFORE UPDATE ON space_capabilities
	FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();

DO $$ BEGIN
	IF EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'update_memberships_updated_at') THEN
		EXECUTE 'DROP TRIGGER update_memberships_updated_at ON memberships';
	END IF;
END $$;
CREATE TRIGGER update_memberships_updated_at BEFORE UPDATE ON memberships
	FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();

-- Insert a default site if none exists
INSERT INTO sites (name)
SELECT 'Default Site'
WHERE NOT EXISTS (SELECT 1 FROM sites);
