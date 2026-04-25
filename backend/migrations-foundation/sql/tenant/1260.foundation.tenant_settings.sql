-- Per-tenant admin-configurable settings. Mirror of site_settings shape but
-- scoped to the tenant database it lives in.

CREATE TABLE IF NOT EXISTS public.tenant_settings (
    key        character varying(100)   PRIMARY KEY,
    value      text                     NOT NULL,
    category   character varying(50)    NOT NULL,
    updated_at timestamp with time zone NOT NULL DEFAULT NOW()
);

COMMENT ON TABLE  public.tenant_settings           IS 'Admin-configurable parameters per tenant';
COMMENT ON COLUMN public.tenant_settings.key       IS 'Dot-separated setting key, e.g. invitations.invitation_expiry_days';
COMMENT ON COLUMN public.tenant_settings.category  IS 'Grouping for UI: security, branding, uploads, search, invitations, rate_limiting';

CREATE INDEX idx_tenant_settings_category ON public.tenant_settings (category);
