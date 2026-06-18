-- @migration-class: expand

-- Bounds the lifetime of the GDPR lifecycle confirm-activity token. The token
-- (lifecycle_confirm_token) was previously valid forever, so a leaked warning
-- email could re-activate an account on the deletion path long after the fact.
-- Adds a nullable expiry column; the issuing worker stamps it and the
-- confirm-activity endpoint rejects expired tokens. Purely additive — existing
-- rows and old code paths are unaffected, so this is rollback-safe.

ALTER TABLE public.users
    ADD COLUMN lifecycle_confirm_token_expires_at timestamp with time zone;

-- Backfill any in-flight warned tokens so they keep working until the next
-- warning regenerates them, instead of being invalidated immediately. Derived
-- from when the warning was last sent (the token is always issued together with
-- lifecycle_last_warned_at).
UPDATE public.users
SET lifecycle_confirm_token_expires_at = lifecycle_last_warned_at + INTERVAL '30 days'
WHERE lifecycle_confirm_token IS NOT NULL
  AND lifecycle_last_warned_at IS NOT NULL;
