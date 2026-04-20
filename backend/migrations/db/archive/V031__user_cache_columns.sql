-- Update tenant users table to be a proper user cache
-- This table is synced from control_plane and used for FK references

-- Add display_name column for showing user names in UI
DO $$ 
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns 
        WHERE table_name = 'users' AND column_name = 'display_name'
    ) THEN
        ALTER TABLE users ADD COLUMN display_name VARCHAR(255);
        RAISE NOTICE 'Added display_name column to users table';
    END IF;
END $$;

-- Add synced_at column to track when user was last synced from control plane
DO $$ 
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns 
        WHERE table_name = 'users' AND column_name = 'synced_at'
    ) THEN
        ALTER TABLE users ADD COLUMN synced_at TIMESTAMPTZ DEFAULT NOW();
        RAISE NOTICE 'Added synced_at column to users table';
    END IF;
END $$;

-- Add comment to clarify this is a cache table
COMMENT ON TABLE users IS 'User cache table - synced from control_plane for FK references. Source of truth is control_plane.users + tenant_memberships.';
