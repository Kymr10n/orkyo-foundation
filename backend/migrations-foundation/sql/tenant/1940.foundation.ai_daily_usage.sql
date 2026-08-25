-- @migration-class: expand

-- Assistant turns taken per subject per day.
--
-- Separate from ai_usage, which counts tokens per user per month for billing. This counts
-- *attempts* per day for rate limiting, and it is keyed by subject rather than user id:
-- the public demo shares one account, so a per-user key there would be a single bucket for
-- every visitor at once. The subject is the identity provider's session id for such shared
-- logins, and the user id otherwise.
--
-- A session id is cheap to replace: a visitor who signs in to the demo again gets a new
-- one, and with it a new bucket. The per-person limit therefore slows a shared account
-- down rather than capping it, and the workspace-wide limit is the ceiling that holds.
-- Set both on an account many people use.
--
-- The day column IS the reset, exactly as the month column is in ai_usage: a new UTC day
-- lands on a new row, so there is no scheduled job to run and none that can fail to.
--
-- Rows are never deleted here. They are small, they are the only record of demo usage, and
-- the tenant database is reseeded on its own schedule.

BEGIN;

CREATE TABLE IF NOT EXISTS public.ai_daily_usage (
    subject text    NOT NULL,
    day     date    NOT NULL,
    turns   integer NOT NULL DEFAULT 0,
    PRIMARY KEY (subject, day)
);

-- The workspace-wide daily total sums across subjects for one day.
CREATE INDEX IF NOT EXISTS idx_ai_daily_usage_day ON public.ai_daily_usage (day);

-- The limits the counts are checked against. A workspace administrator sets them in the
-- AI Assistant tab; NULL means no limit, which is the state every workspace starts in.
-- One row per workspace, enforced the same way ai_credentials enforces its singleton.
CREATE TABLE IF NOT EXISTS public.ai_daily_limits (
    singleton           boolean     PRIMARY KEY DEFAULT true CHECK (singleton),
    user_daily_turns    integer     NULL CHECK (user_daily_turns > 0),
    tenant_daily_turns  integer     NULL CHECK (tenant_daily_turns > 0),
    updated_at          timestamptz NOT NULL DEFAULT NOW(),
    updated_by_user_id  uuid
);

COMMIT;

-- Rollback: DROP TABLE public.ai_daily_usage; DROP TABLE public.ai_daily_limits;
