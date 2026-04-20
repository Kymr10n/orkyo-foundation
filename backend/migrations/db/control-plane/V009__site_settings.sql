-- Site-level settings (platform-wide, not per-tenant).
-- Same schema as tenant_settings but stored in control_plane.
-- Examples: rate limits, brute-force thresholds, upload constraints.

CREATE TABLE IF NOT EXISTS site_settings (
    key        VARCHAR(100) PRIMARY KEY,
    value      TEXT         NOT NULL,
    category   VARCHAR(50)  NOT NULL DEFAULT 'general',
    updated_at TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

COMMENT ON TABLE  site_settings IS 'Platform-wide settings managed by site administrators';
COMMENT ON COLUMN site_settings.key IS 'Dot-separated setting key, e.g. security.brute_force_lockout_threshold';
COMMENT ON COLUMN site_settings.value IS 'Current override value (text). Parsed by the application layer.';
