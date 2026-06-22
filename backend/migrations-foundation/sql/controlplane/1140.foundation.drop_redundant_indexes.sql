-- @migration-class: contract

-- Drop control-plane indexes that are fully covered by an existing UNIQUE/PK
-- backing index, so they only add write-amplification and bloat.
--
--   idx_users_email             → covered by users_email_key UNIQUE (email)
--   idx_tenants_slug            → covered by tenants_slug_key UNIQUE (slug)
--   idx_invitations_token_hash  → covered by invitations_token_hash_key UNIQUE (token_hash)
--   idx_tos_acceptances_user_id → left-prefix of unique (user_id, tos_version)
--
-- Each constraint's implicit index already serves equality lookups on the same
-- column(s), so the planner never needs the duplicate. Rollback: recreate the
-- dropped indexes (all non-unique btrees on the listed columns).

BEGIN;

DROP INDEX IF EXISTS public.idx_users_email;
DROP INDEX IF EXISTS public.idx_tenants_slug;
DROP INDEX IF EXISTS public.idx_invitations_token_hash;
DROP INDEX IF EXISTS public.idx_tos_acceptances_user_id;

COMMIT;
