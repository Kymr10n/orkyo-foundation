-- @migration-class: expand

-- Two assistant settings.
--
-- ai_daily_limits.private_chat: the workspace keeps no conversations. The panel stops
-- saving, the server refuses a save, and turning it on deletes the stored ones. The
-- public demo runs with it on, because every visitor shares one account.
--
-- ai_credentials.rejected_at: when the provider last refused the key (revoked, or no
-- credit left). The assistant reports itself unavailable for an hour after a refusal, then
-- one turn tries again, so topped-up credit recovers without an administrator.

BEGIN;

ALTER TABLE public.ai_daily_limits ADD COLUMN IF NOT EXISTS private_chat boolean NOT NULL DEFAULT false;
ALTER TABLE public.ai_credentials ADD COLUMN IF NOT EXISTS rejected_at timestamptz NULL;

COMMIT;

-- Rollback: ALTER TABLE public.ai_daily_limits DROP COLUMN private_chat;
--           ALTER TABLE public.ai_credentials DROP COLUMN rejected_at;
