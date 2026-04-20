-- Track whether a user has seen the onboarding tour.
-- Global per user (control-plane), not per tenant.
-- Once set to true it is never reset automatically; the UI
-- always allows retaking the tour from the user menu.

ALTER TABLE public.users
    ADD COLUMN has_seen_tour BOOLEAN NOT NULL DEFAULT false;
