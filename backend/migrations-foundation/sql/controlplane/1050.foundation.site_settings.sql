-- Site-level settings — platform-wide overrides managed by site administrators.

CREATE TABLE IF NOT EXISTS public.site_settings (
    key        character varying(100)   PRIMARY KEY,
    value      text                     NOT NULL,
    category   character varying(50)    NOT NULL DEFAULT 'general',
    updated_at timestamp with time zone NOT NULL DEFAULT NOW()
);

COMMENT ON TABLE  public.site_settings          IS 'Platform-wide settings managed by site administrators';
COMMENT ON COLUMN public.site_settings.key      IS 'Dot-separated setting key, e.g. security.brute_force_lockout_threshold';
COMMENT ON COLUMN public.site_settings.value    IS 'Current override value (text). Parsed by the application layer.';
