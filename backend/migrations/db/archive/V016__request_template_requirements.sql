-- Add requirements support to request templates
-- This allows templates to include pre-configured criterion requirements

CREATE TABLE request_template_requirements (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    template_id UUID NOT NULL REFERENCES request_templates(id) ON DELETE CASCADE,
    criterion_id UUID NOT NULL REFERENCES criteria(id) ON DELETE CASCADE,
    value JSONB NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    
    -- Ensure each criterion appears only once per template
    UNIQUE(template_id, criterion_id)
);

-- Index for faster lookups by template
CREATE INDEX idx_template_requirements_template_id ON request_template_requirements(template_id);

-- Index for faster lookups by criterion
CREATE INDEX idx_template_requirements_criterion_id ON request_template_requirements(criterion_id);
