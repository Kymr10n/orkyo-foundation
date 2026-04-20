-- Add role column to users table in tenant databases
-- Applied to tenant databases to enable RBAC

DO $$ 
BEGIN
    -- Add role column if it doesn't exist
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'users' AND column_name = 'role') THEN
        ALTER TABLE users 
        ADD COLUMN role VARCHAR(30) NOT NULL DEFAULT 'inactive' 
        CHECK (role IN ('admin', 'editor', 'viewer', 'inactive'));
        
        -- Create index for role-based queries
        CREATE INDEX idx_users_role ON users(role);
        
        RAISE NOTICE 'Added role column to users table';
    END IF;
    
    -- Add password_hash column if it doesn't exist (for direct user creation)
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'users' AND column_name = 'password_hash') THEN
        ALTER TABLE users 
        ADD COLUMN password_hash VARCHAR(255);
        
        RAISE NOTICE 'Added password_hash column to users table';
    END IF;
END $$;
