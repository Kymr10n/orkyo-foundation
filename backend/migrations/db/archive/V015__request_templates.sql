-- Request Templates: Reusable templates for creating requests with predefined values
CREATE TABLE request_templates (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    name VARCHAR(200) NOT NULL,
    description TEXT,
    duration_value INTEGER NOT NULL CHECK (duration_value > 0),
    duration_unit VARCHAR(20) NOT NULL CHECK (duration_unit IN ('minutes', 'hours', 'days', 'weeks')),
    fixed_start BOOLEAN NOT NULL DEFAULT false,
    fixed_end BOOLEAN NOT NULL DEFAULT false,
    fixed_duration BOOLEAN NOT NULL DEFAULT true,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
);

-- Index for fast lookups by user
CREATE INDEX idx_request_templates_user_id ON request_templates(user_id);

-- Ensure template names are unique per user
CREATE UNIQUE INDEX idx_request_templates_user_name ON request_templates(user_id, name);

-- Add updated_at trigger
CREATE TRIGGER update_request_templates_updated_at
    BEFORE UPDATE ON request_templates
    FOR EACH ROW
    EXECUTE FUNCTION update_updated_at_column();
