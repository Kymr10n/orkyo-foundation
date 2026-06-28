-- @migration-class: expand
-- Multi-channel announcement delivery: in-app ('site') + 'email'.
-- Independent channels per announcement; email is broadcast asynchronously by the worker.

ALTER TABLE public.announcements
    ADD COLUMN channels      text[]                   NOT NULL DEFAULT ARRAY['site']::text[],
    ADD COLUMN email_sent_at timestamp with time zone NULL;

COMMENT ON COLUMN public.announcements.channels      IS 'Delivery channels: subset of {site, email}. site = in-app notification center.';
COMMENT ON COLUMN public.announcements.email_sent_at IS 'Set once the email broadcast for this announcement has completed (idempotency marker for the worker).';

-- Per-user opt-out for announcement emails + a stable token for the unsubscribe link.
ALTER TABLE public.users
    ADD COLUMN announcement_email_opt_out boolean NOT NULL DEFAULT FALSE,
    ADD COLUMN unsubscribe_token          uuid    NOT NULL DEFAULT gen_random_uuid();

COMMENT ON COLUMN public.users.announcement_email_opt_out IS 'When true, the user is excluded from announcement email broadcasts.';
COMMENT ON COLUMN public.users.unsubscribe_token          IS 'Stable per-user token embedded in the announcement-email unsubscribe link.';

CREATE UNIQUE INDEX idx_users_unsubscribe_token ON public.users (unsubscribe_token);
