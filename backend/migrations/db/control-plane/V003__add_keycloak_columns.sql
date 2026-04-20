-- Add Keycloak integration columns to users table
-- These columns remain unused until Keycloak is integrated

-- Add keycloak_id column (nullable for migration, will be populated when Keycloak is integrated)
ALTER TABLE public.users
ADD COLUMN keycloak_id VARCHAR(255) NULL;

-- Add keycloak_metadata for storing additional Keycloak claims (groups, roles, etc)
ALTER TABLE public.users
ADD COLUMN keycloak_metadata JSONB NULL;

-- Create unique index on keycloak_id (excludes nulls)
CREATE UNIQUE INDEX idx_users_keycloak_id
ON public.users (keycloak_id)
WHERE keycloak_id IS NOT NULL;

-- Add comment for documentation
COMMENT ON COLUMN public.users.keycloak_id IS 'Keycloak user subject identifier (sub claim)';
COMMENT ON COLUMN public.users.keycloak_metadata IS 'Additional Keycloak user claims and attributes';
