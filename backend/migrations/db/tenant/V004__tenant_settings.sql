-- Tenant settings: key/value store for admin-configurable parameters
-- Each row is scoped to the tenant database it lives in.

CREATE TABLE IF NOT EXISTS tenant_settings (
    key         VARCHAR(100) PRIMARY KEY,
    value       TEXT         NOT NULL,
    category    VARCHAR(50)  NOT NULL,
    updated_at  TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

COMMENT ON TABLE  tenant_settings IS 'Admin-configurable parameters per tenant';
COMMENT ON COLUMN tenant_settings.key      IS 'Dot-separated setting key, e.g. invitations.invitation_expiry_days';
COMMENT ON COLUMN tenant_settings.category IS 'Grouping for UI: security, branding, uploads, search, invitations, rate_limiting';

CREATE INDEX idx_tenant_settings_category ON tenant_settings (category);
