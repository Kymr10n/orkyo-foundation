-- Migration: Add group capabilities
-- Similar to space_capabilities but defines default/inherited capabilities for all spaces in a group

-- Create group_capabilities table
CREATE TABLE IF NOT EXISTS group_capabilities (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    group_id UUID NOT NULL REFERENCES space_groups(id) ON DELETE CASCADE,
    criterion_id UUID NOT NULL REFERENCES criteria(id) ON DELETE CASCADE,
    value JSONB NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT unique_group_criterion UNIQUE (group_id, criterion_id)
);

-- Create indexes
CREATE INDEX IF NOT EXISTS idx_group_capabilities_group_id ON group_capabilities(group_id);
CREATE INDEX IF NOT EXISTS idx_group_capabilities_criterion_id ON group_capabilities(criterion_id);

-- Add trigger for updated_at
CREATE TRIGGER update_group_capabilities_updated_at BEFORE UPDATE ON group_capabilities
    FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();
