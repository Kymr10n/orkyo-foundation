-- Migration: Unified Template System
-- Replaces request_templates with generic templates supporting requests, spaces, and groups

-- Create generic templates table
CREATE TABLE IF NOT EXISTS templates (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name VARCHAR(255) NOT NULL,
    description TEXT,
    entity_type VARCHAR(20) NOT NULL CHECK (entity_type IN ('request', 'space', 'group')),
    
    -- Request-specific fields (only used when entity_type = 'request')
    duration_value INTEGER CHECK (duration_value IS NULL OR duration_value > 0),
    duration_unit VARCHAR(20) CHECK (duration_unit IS NULL OR duration_unit IN ('minutes', 'hours', 'days', 'weeks')),
    fixed_start BOOLEAN DEFAULT false,
    fixed_end BOOLEAN DEFAULT false,
    fixed_duration BOOLEAN DEFAULT true,
    
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Create template items table (criterion-value pairs)
CREATE TABLE IF NOT EXISTS template_items (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    template_id UUID NOT NULL REFERENCES templates(id) ON DELETE CASCADE,
    criterion_id UUID NOT NULL REFERENCES criteria(id) ON DELETE CASCADE,
    value JSONB NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT unique_template_criterion UNIQUE (template_id, criterion_id)
);

-- Create indexes
CREATE INDEX IF NOT EXISTS idx_templates_entity_type ON templates(entity_type);
CREATE INDEX IF NOT EXISTS idx_templates_name ON templates(name);
CREATE INDEX IF NOT EXISTS idx_template_items_template_id ON template_items(template_id);
CREATE INDEX IF NOT EXISTS idx_template_items_criterion_id ON template_items(criterion_id);

-- Add triggers for updated_at
CREATE TRIGGER update_templates_updated_at 
    BEFORE UPDATE ON templates
    FOR EACH ROW 
    EXECUTE FUNCTION update_updated_at_column();

CREATE TRIGGER update_template_items_updated_at 
    BEFORE UPDATE ON template_items
    FOR EACH ROW 
    EXECUTE FUNCTION update_updated_at_column();

-- Migrate existing request_templates data with corrected column names
INSERT INTO templates (
    id,
    name,
    description,
    entity_type,
    duration_value,
    duration_unit,
    fixed_start,
    fixed_end,
    fixed_duration,
    created_at,
    updated_at
)
SELECT 
    id,
    name,
    description,
    'request'::VARCHAR(20) as entity_type,
    minimal_duration_value,
    minimal_duration_unit,
    false, -- fixed_start (these columns were removed)
    false, -- fixed_end  
    true,  -- fixed_duration
    created_at,
    updated_at
FROM request_templates
WHERE EXISTS (SELECT 1 FROM request_templates);

-- Migrate request_template_requirements data
INSERT INTO template_items (
    id,
    template_id,
    criterion_id,
    value,
    created_at
)
SELECT 
    id,
    template_id,
    criterion_id,
    value,
    created_at
FROM request_template_requirements
WHERE EXISTS (SELECT 1 FROM request_template_requirements);

-- Drop old tables (keeping for rollback safety - uncomment after verification)
-- DROP TABLE IF EXISTS request_template_requirements;
-- DROP TABLE IF EXISTS request_templates;
