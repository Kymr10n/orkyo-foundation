-- Migration: Add space groups
-- Groups are tenant-wide and can span multiple sites
-- Spaces can belong to zero or one group

-- Create space_groups table
CREATE TABLE IF NOT EXISTS space_groups (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name VARCHAR(255) NOT NULL,
    description TEXT,
    color VARCHAR(7), -- Hex color for visual grouping (#RRGGBB)
    display_order INTEGER NOT NULL DEFAULT 0,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Add group_id to spaces table
ALTER TABLE spaces ADD COLUMN IF NOT EXISTS group_id UUID REFERENCES space_groups(id) ON DELETE SET NULL;

-- Create index for group lookups
CREATE INDEX IF NOT EXISTS idx_spaces_group_id ON spaces(group_id);

-- Create index for display order
CREATE INDEX IF NOT EXISTS idx_space_groups_display_order ON space_groups(display_order);

-- Add trigger for updated_at
CREATE TRIGGER update_space_groups_updated_at BEFORE UPDATE ON space_groups
    FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();
