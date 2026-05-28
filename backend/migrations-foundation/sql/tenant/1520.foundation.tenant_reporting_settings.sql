-- @migration-class: expand
-- Description: Seed default reporting.people_level_enabled setting (false = aggregate by group)
INSERT INTO tenant_settings (key, value, category)
VALUES ('reporting.people_level_enabled', 'false', 'reporting')
ON CONFLICT (key) DO NOTHING;
