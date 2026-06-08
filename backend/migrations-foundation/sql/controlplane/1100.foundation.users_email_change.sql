ALTER TABLE public.users
    ADD COLUMN pending_email              character varying(320),
    ADD COLUMN email_change_token         character varying(36),
    ADD COLUMN email_change_requested_at  timestamp with time zone;

CREATE UNIQUE INDEX idx_users_email_change_token
    ON public.users (email_change_token)
    WHERE email_change_token IS NOT NULL;

CREATE UNIQUE INDEX idx_users_pending_email_lower
    ON public.users (lower(pending_email))
    WHERE pending_email IS NOT NULL;
