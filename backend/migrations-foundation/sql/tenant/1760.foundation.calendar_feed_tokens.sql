-- @migration-class: expand

-- Per-user calendar subscription tokens, so a user can add their Orkyo schedule to
-- Outlook (or Google, or Apple) as a live feed.
--
-- Why a token and not the session: a calendar client fetches the URL unattended, on
-- its own schedule, with no cookie jar and no way to complete an OIDC redirect. The
-- URL itself has to be the credential. That makes the token a bearer secret, so:
--
--   * only a SHA-256 hash is stored. A leaked database gives an attacker no working
--     feed URL, exactly as for a password.
--   * it is scoped to one user and revocable on its own, without touching their
--     login. Revoking is the only remedy once a URL has leaked into a shared
--     calendar, so it must be one click and immediate.
--   * last_used_at records that a client is actually polling, which is what makes an
--     unused token safe to delete.
--
-- The feed is read-only: the token authorizes GET of that user's schedule and nothing
-- else. It carries no roles.
--
-- Rollback: drop the table.

BEGIN;

CREATE TABLE IF NOT EXISTS public.calendar_feed_tokens (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id         UUID NOT NULL,
    -- SHA-256 of the token, hex-encoded. Never the token itself.
    token_hash      CHAR(64) NOT NULL,
    -- What the user called it ("Outlook, laptop"), so several devices stay tellable apart.
    label           VARCHAR(100),
    -- Optional single-site scope; NULL means every site the user can see.
    site_id         UUID REFERENCES public.sites(id) ON DELETE CASCADE,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    last_used_at    TIMESTAMPTZ,
    revoked_at      TIMESTAMPTZ
);

-- The feed request arrives with only the token, so this lookup is the hot path.
CREATE UNIQUE INDEX IF NOT EXISTS ix_calendar_feed_tokens_hash
    ON public.calendar_feed_tokens (token_hash);

-- The settings page lists a user's own tokens.
CREATE INDEX IF NOT EXISTS ix_calendar_feed_tokens_user
    ON public.calendar_feed_tokens (user_id, created_at DESC);

COMMENT ON TABLE public.calendar_feed_tokens IS
    'Bearer tokens for read-only iCalendar subscriptions. Hash-at-rest; one row per '
    'subscription so a single device can be revoked without disturbing the others.';

COMMIT;
