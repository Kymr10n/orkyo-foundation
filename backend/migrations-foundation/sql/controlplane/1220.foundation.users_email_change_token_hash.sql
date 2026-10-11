-- @migration-class: expand

-- Stores the email-change confirmation token as a SHA-256 digest (64 lowercase
-- hex characters) instead of in plaintext, like every other token in the
-- control plane. Adds a nullable column and its partial unique index. The old
-- email_change_token column stays: code no longer reads or writes it, and a
-- later release drops it. Purely additive, so this is rollback-safe.

ALTER TABLE public.users
    ADD COLUMN email_change_token_hash character varying(64);

CREATE UNIQUE INDEX CONCURRENTLY idx_users_email_change_token_hash
    ON public.users (email_change_token_hash)
    WHERE email_change_token_hash IS NOT NULL;
