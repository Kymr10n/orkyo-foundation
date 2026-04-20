-- Create tenant_memberships table for multi-tenant user management
-- This implements the centralized membership model where users can belong to multiple tenants

-- Tenant Memberships: Links users to tenants with per-tenant role and status
CREATE TABLE IF NOT EXISTS tenant_memberships (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    tenant_id UUID NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    role VARCHAR(30) NOT NULL DEFAULT 'viewer' 
        CHECK (role IN ('admin', 'editor', 'viewer', 'inactive')),
    status VARCHAR(30) NOT NULL DEFAULT 'active'
        CHECK (status IN ('active', 'disabled')),
    invited_by UUID REFERENCES users(id) ON DELETE SET NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE(user_id, tenant_id)
);

CREATE INDEX IF NOT EXISTS idx_tenant_memberships_user_id ON tenant_memberships(user_id);
CREATE INDEX IF NOT EXISTS idx_tenant_memberships_tenant_id ON tenant_memberships(tenant_id);
CREATE INDEX IF NOT EXISTS idx_tenant_memberships_role ON tenant_memberships(role);

-- Add tenant_id to invitations table if not exists
DO $$ 
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns 
        WHERE table_name = 'invitations' AND column_name = 'tenant_id'
    ) THEN
        ALTER TABLE invitations 
        ADD COLUMN tenant_id UUID REFERENCES tenants(id) ON DELETE CASCADE;
        
        CREATE INDEX idx_invitations_tenant_id ON invitations(tenant_id);
        
        RAISE NOTICE 'Added tenant_id column to invitations table';
    END IF;
END $$;

-- Remove role and status from users table if they exist (now in tenant_memberships)
-- We keep them for backward compatibility but mark as deprecated
-- Future migration will remove them after full transition
COMMENT ON COLUMN users.role IS 'DEPRECATED: Use tenant_memberships.role instead';
COMMENT ON COLUMN users.status IS 'DEPRECATED: Use user status in tenant_memberships instead. This column now only tracks email verification status.';

-- Migrate existing users to tenant_memberships for the demo tenant
-- This creates memberships for all existing users
DO $$
DECLARE
    demo_tenant_id UUID;
BEGIN
    -- Get the demo tenant ID
    SELECT id INTO demo_tenant_id FROM tenants WHERE slug = 'demo' LIMIT 1;
    
    IF demo_tenant_id IS NOT NULL THEN
        -- Create memberships for all existing users who don't have one
        INSERT INTO tenant_memberships (user_id, tenant_id, role, status)
        SELECT 
            u.id,
            demo_tenant_id,
            COALESCE(u.role, 'viewer'),
            CASE 
                WHEN u.status = 'active' THEN 'active'
                WHEN u.status = 'disabled' THEN 'disabled'
                ELSE 'active'
            END
        FROM users u
        WHERE u.id != '00000000-0000-0000-0000-000000000000'  -- Exclude system user
          AND NOT EXISTS (
              SELECT 1 FROM tenant_memberships tm 
              WHERE tm.user_id = u.id AND tm.tenant_id = demo_tenant_id
          );
        
        RAISE NOTICE 'Migrated existing users to tenant_memberships for demo tenant';
    END IF;
END $$;

-- Update existing invitations to have tenant_id for demo tenant
DO $$
DECLARE
    demo_tenant_id UUID;
BEGIN
    SELECT id INTO demo_tenant_id FROM tenants WHERE slug = 'demo' LIMIT 1;
    
    IF demo_tenant_id IS NOT NULL THEN
        UPDATE invitations 
        SET tenant_id = demo_tenant_id
        WHERE tenant_id IS NULL;
        
        RAISE NOTICE 'Updated existing invitations with demo tenant_id';
    END IF;
END $$;
