-- V004: Add Keycloak provider support for OAuth identities
-- This migration enables Keycloak as an identity provider

-- Drop the existing constraint
ALTER TABLE public.user_identities 
DROP CONSTRAINT IF EXISTS user_identities_provider_check;

-- Add new constraint with keycloak included
ALTER TABLE public.user_identities 
ADD CONSTRAINT user_identities_provider_check 
CHECK (provider IN ('google', 'azure', 'github', 'keycloak'));

-- Add index on provider_subject for faster Keycloak lookups
CREATE INDEX IF NOT EXISTS idx_user_identities_keycloak_subject 
ON public.user_identities(provider_subject) 
WHERE provider = 'keycloak';

COMMENT ON TABLE public.user_identities IS 
'Links external identity provider subjects (OAuth/OIDC) to internal users. Supports Google, Azure, GitHub, and Keycloak providers.';
