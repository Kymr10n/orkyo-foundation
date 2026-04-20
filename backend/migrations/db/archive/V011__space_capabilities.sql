-- Migration: Add space_capabilities table and sample data
-- Applied to the database specified by the migration script

CREATE TABLE IF NOT EXISTS space_capabilities (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    space_id UUID NOT NULL REFERENCES spaces(id) ON DELETE CASCADE,
    criterion_id UUID NOT NULL REFERENCES criteria(id) ON DELETE CASCADE,
    value JSONB NOT NULL, -- Stores the actual value: true/false for Boolean, number for Number, string for String/Enum
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT unique_space_criterion UNIQUE (space_id, criterion_id)
);

CREATE INDEX IF NOT EXISTS idx_space_capabilities_space_id ON space_capabilities(space_id);
CREATE INDEX IF NOT EXISTS idx_space_capabilities_criterion_id ON space_capabilities(criterion_id);

-- Create trigger if it doesn't exist (may already exist from V001)
DO $$ 
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'update_space_capabilities_updated_at') THEN
        CREATE TRIGGER update_space_capabilities_updated_at BEFORE UPDATE ON space_capabilities
            FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();
    END IF;
END $$;

-- Add sample capabilities for test spaces
-- Zone A-01: power_400v=true, ethernet=true, max_load_kg=5000
INSERT INTO space_capabilities (space_id, criterion_id, value)
SELECT 
    s.id,
    c.id,
    CASE 
        WHEN c.name = 'power_400v' THEN 'true'::jsonb
        WHEN c.name = 'ethernet' THEN 'true'::jsonb
        WHEN c.name = 'max_load_kg' THEN '5000'::jsonb
    END
FROM spaces s
CROSS JOIN criteria c
WHERE s.code = 'A-01'
  AND c.name IN ('power_400v', 'ethernet', 'max_load_kg')
ON CONFLICT (space_id, criterion_id) DO NOTHING;

-- Zone A-02: power_230v=true, compressed_air=true, floor_area_m2=25
INSERT INTO space_capabilities (space_id, criterion_id, value)
SELECT 
    s.id,
    c.id,
    CASE 
        WHEN c.name = 'power_230v' THEN 'true'::jsonb
        WHEN c.name = 'compressed_air' THEN 'true'::jsonb
        WHEN c.name = 'floor_area_m2' THEN '25'::jsonb
    END
FROM spaces s
CROSS JOIN criteria c
WHERE s.code = 'A-02'
  AND c.name IN ('power_230v', 'compressed_air', 'floor_area_m2')
ON CONFLICT (space_id, criterion_id) DO NOTHING;

-- Zone A-03: ethernet=true, climate_controlled=true, size_class="M"
INSERT INTO space_capabilities (space_id, criterion_id, value)
SELECT 
    s.id,
    c.id,
    CASE 
        WHEN c.name = 'ethernet' THEN 'true'::jsonb
        WHEN c.name = 'climate_controlled' THEN 'true'::jsonb
        WHEN c.name = 'size_class' THEN '"M"'::jsonb
    END
FROM spaces s
CROSS JOIN criteria c
WHERE s.code = 'A-03'
  AND c.name IN ('ethernet', 'climate_controlled', 'size_class')
ON CONFLICT (space_id, criterion_id) DO NOTHING;
