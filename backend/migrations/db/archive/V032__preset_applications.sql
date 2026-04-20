-- ============================================================================
-- V032: Add Preset Application Tables
-- ============================================================================
-- Purpose: Track preset applications and map logical keys to database entities
--          for idempotent preset re-application.
-- ============================================================================

-- Preset applications: tracks which presets have been applied to this tenant
CREATE TABLE IF NOT EXISTS preset_applications (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    preset_id VARCHAR(100) NOT NULL,
    preset_version VARCHAR(20) NOT NULL,
    applied_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMPTZ,
    applied_by_user_id UUID REFERENCES users(id) ON DELETE SET NULL,
    
    CONSTRAINT uq_preset_applications_preset_id UNIQUE (preset_id)
);

-- Index for looking up preset applications
CREATE INDEX idx_preset_applications_preset_id ON preset_applications(preset_id);

-- Preset mappings: maps logical keys from presets to database entity IDs
CREATE TABLE IF NOT EXISTS preset_mappings (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    preset_application_id UUID NOT NULL REFERENCES preset_applications(id) ON DELETE CASCADE,
    entity_type VARCHAR(50) NOT NULL,
    logical_key VARCHAR(100) NOT NULL,
    entity_id UUID NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    
    CONSTRAINT uq_preset_mappings_key UNIQUE (preset_application_id, entity_type, logical_key)
);

-- Index for looking up mappings by application
CREATE INDEX idx_preset_mappings_application ON preset_mappings(preset_application_id);

-- Index for looking up mappings by entity
CREATE INDEX idx_preset_mappings_entity ON preset_mappings(entity_type, entity_id);

COMMENT ON TABLE preset_applications IS 'Tracks which presets have been applied to this tenant';
COMMENT ON TABLE preset_mappings IS 'Maps logical keys from presets to database entity IDs for idempotent updates';
COMMENT ON COLUMN preset_applications.preset_id IS 'Unique identifier from the preset file (e.g., manufacturing-ch-v1)';
COMMENT ON COLUMN preset_mappings.entity_type IS 'Type of entity: criterion, space_group, template_space, template_group, template_request';
COMMENT ON COLUMN preset_mappings.logical_key IS 'The key from the preset file (e.g., shift-model)';
