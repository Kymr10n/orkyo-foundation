-- Remove legacy password-based authentication tables and columns.
-- All authentication is now handled by Keycloak via OIDC.
-- The user_credentials table stored local BCrypt hashes; no longer needed.
-- The password_hash column on users was a redundant legacy field.

DROP TABLE IF EXISTS public.user_credentials;

ALTER TABLE public.users
    DROP COLUMN IF EXISTS password_hash;
