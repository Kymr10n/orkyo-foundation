-- Add service tier support to tenants table and create interest_registrations table

-- Add tier column to tenants table with Free as default
ALTER TABLE public.tenants
ADD COLUMN tier integer DEFAULT 0 NOT NULL;

-- Add check constraint to ensure valid tier values (0=Free, 1=Professional, 2=Enterprise)
ALTER TABLE public.tenants
ADD CONSTRAINT tenants_tier_check CHECK (tier >= 0 AND tier <= 2);

-- Create index on tier column for potential future queries
CREATE INDEX idx_tenants_tier ON public.tenants USING btree (tier);

-- Create interest_registrations table for tracking upgrade interest
CREATE TABLE public.interest_registrations (
    id uuid DEFAULT gen_random_uuid() NOT NULL PRIMARY KEY,
    email character varying(320) NOT NULL,
    organization_id uuid,
    tier integer NOT NULL,
    created_at_utc timestamp with time zone DEFAULT now() NOT NULL,
    source character varying(100) NOT NULL,
    CONSTRAINT interest_registrations_tier_check CHECK (tier >= 1 AND tier <= 2)
);

-- Add index on email for duplicate checking
CREATE INDEX idx_interest_registrations_email ON public.interest_registrations USING btree (email);

-- Add index on tier for reporting
CREATE INDEX idx_interest_registrations_tier ON public.interest_registrations USING btree (tier);

-- Add index on created_at_utc for time-based queries
CREATE INDEX idx_interest_registrations_created_at ON public.interest_registrations USING btree (created_at_utc);

-- Add foreign key constraint if organization_id points to tenants
ALTER TABLE public.interest_registrations
ADD CONSTRAINT interest_registrations_organization_id_fkey FOREIGN KEY (organization_id) REFERENCES public.tenants(id) ON DELETE SET NULL;

-- Add comment to document the purpose
COMMENT ON TABLE public.interest_registrations IS 'Tracks user interest in Professional and Enterprise service tiers for sales/marketing follow-up';
COMMENT ON COLUMN public.tenants.tier IS 'Service tier: 0=Free, 1=Professional, 2=Enterprise';
