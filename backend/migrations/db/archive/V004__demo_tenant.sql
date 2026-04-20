-- Migration: Demo tenant seed data
-- Inserts demo tenant into tenants table (control_plane database only)
-- This creates the 'demo' tenant that points to tenant_demo database

INSERT INTO tenants (slug, display_name, status, db_identifier, created_at, updated_at)
VALUES (
    'demo',
    'Demo Organization',
    'active',
    'tenant_demo',
    NOW(),
    NOW()
)
ON CONFLICT (slug) DO UPDATE SET
    display_name = EXCLUDED.display_name,
    status = EXCLUDED.status,
    db_identifier = EXCLUDED.db_identifier,
    updated_at = NOW();
