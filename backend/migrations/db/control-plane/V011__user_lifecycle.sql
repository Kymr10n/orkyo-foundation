-- Add GDPR lifecycle management columns to users table.
-- These track inactivity warnings, dormancy state, and the one-time
-- confirmation token sent in warning emails.
--
-- lifecycle_status NULL  = active (no action needed)
-- lifecycle_status = 'warned'  = warning emails are being sent
-- lifecycle_status = 'dormant' = account disabled, pending purge

ALTER TABLE public.users
    ADD COLUMN lifecycle_status VARCHAR(20) NULL,
    ADD COLUMN lifecycle_warning_count SMALLINT NOT NULL DEFAULT 0,
    ADD COLUMN lifecycle_last_warned_at TIMESTAMPTZ NULL,
    ADD COLUMN lifecycle_dormant_since TIMESTAMPTZ NULL,
    ADD COLUMN lifecycle_confirm_token VARCHAR(36) NULL;

ALTER TABLE public.users
    ADD CONSTRAINT users_lifecycle_status_check
    CHECK (lifecycle_status IS NULL OR lifecycle_status IN ('warned', 'dormant'));

CREATE UNIQUE INDEX idx_users_lifecycle_confirm_token
    ON public.users (lifecycle_confirm_token)
    WHERE lifecycle_confirm_token IS NOT NULL;

CREATE INDEX idx_users_lifecycle_status
    ON public.users (lifecycle_status)
    WHERE lifecycle_status IS NOT NULL;
