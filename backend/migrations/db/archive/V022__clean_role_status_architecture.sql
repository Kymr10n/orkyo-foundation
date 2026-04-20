-- V022: Clean up role/status architecture
-- Role = permission level (admin, editor, viewer)
-- Status = membership state (active, pending, disabled)
-- Remove 'inactive' from role (it was wrongly used as a status)

-- Step 1: Drop and recreate the status CHECK constraint FIRST (add 'pending')
-- This must happen before we try to set status = 'pending'
ALTER TABLE tenant_memberships 
DROP CONSTRAINT IF EXISTS tenant_memberships_status_check;

ALTER TABLE tenant_memberships 
ADD CONSTRAINT tenant_memberships_status_check 
CHECK (status IN ('active', 'pending', 'disabled'));

-- Step 2: Migrate any existing 'inactive' roles to 'viewer' role + 'pending' status
UPDATE tenant_memberships 
SET role = 'viewer', status = 'pending', updated_at = NOW()
WHERE role = 'inactive';

-- Step 3: Drop and recreate the role CHECK constraint (remove 'inactive')
ALTER TABLE tenant_memberships 
DROP CONSTRAINT IF EXISTS tenant_memberships_role_check;

ALTER TABLE tenant_memberships 
ADD CONSTRAINT tenant_memberships_role_check 
CHECK (role IN ('admin', 'editor', 'viewer'));

-- Step 4: Drop legacy columns from users table (role and is_tenant_admin are now in tenant_memberships)
ALTER TABLE users DROP COLUMN IF EXISTS role;
ALTER TABLE users DROP COLUMN IF EXISTS is_tenant_admin;

-- Add comment to document the architecture
COMMENT ON COLUMN tenant_memberships.role IS 'Permission level within tenant: admin, editor, viewer';
COMMENT ON COLUMN tenant_memberships.status IS 'Membership state: active, pending (awaiting verification), disabled';
