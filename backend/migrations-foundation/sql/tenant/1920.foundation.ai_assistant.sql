-- @migration-class: expand

-- AI assistant: the workspace's own Anthropic credential, per-user access grants,
-- and per-user monthly token usage.
--
-- ai_credentials holds ONE row (keyed by provider) per tenant database. The API key
-- is stored as an Orkyo string encryption envelope written by IEncryptionService with
-- the tenant id as GCM associated data, so a row copied to another tenant's database
-- fails to decrypt. key_hint is the non-secret tail of the key, for display only —
-- the key itself is never read back out of the API.
--
-- ai_user_allowances is deny-by-default: NO ROW MEANS NO ACCESS. Tenant admins bypass
-- the table entirely and always have access, so a workspace works the moment the
-- credential is saved. monthly_token_limit NULL means unlimited, 0 means blocked.
--
-- ai_usage counts tokens per user per calendar month. The month column IS the reset —
-- a new month starts a new row, so there is no scheduled reset job to run or to fail.

BEGIN;

CREATE TABLE IF NOT EXISTS public.ai_credentials (
    provider            text        PRIMARY KEY CHECK (provider = 'anthropic'),
    api_key_ciphertext  text        NOT NULL,
    key_hint            varchar(12) NOT NULL,
    model               text        NULL,
    created_at          timestamptz NOT NULL DEFAULT NOW(),
    updated_at          timestamptz NOT NULL DEFAULT NOW(),
    created_by_user_id  uuid        NULL,
    last_verified_at    timestamptz NULL
);

CREATE TABLE IF NOT EXISTS public.ai_user_allowances (
    user_id             uuid        PRIMARY KEY,
    monthly_token_limit bigint      NULL CHECK (monthly_token_limit IS NULL OR monthly_token_limit >= 0),
    updated_at          timestamptz NOT NULL DEFAULT NOW(),
    updated_by_user_id  uuid        NULL
);

CREATE TABLE IF NOT EXISTS public.ai_usage (
    user_id       uuid    NOT NULL,
    month         date    NOT NULL,
    input_tokens  bigint  NOT NULL DEFAULT 0,
    output_tokens bigint  NOT NULL DEFAULT 0,
    turns         integer NOT NULL DEFAULT 0,
    PRIMARY KEY (user_id, month)
);

COMMIT;

-- Rollback: DROP TABLE public.ai_usage, public.ai_user_allowances, public.ai_credentials;
