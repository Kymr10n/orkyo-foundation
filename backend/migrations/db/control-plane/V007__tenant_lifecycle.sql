-- Migration: Add tenant lifecycle tracking columns
-- Purpose: Track last activity for dormancy detection and suspension workflow

-- Add last_activity_at to track when tenant was last used
ALTER TABLE public.tenants 
ADD COLUMN last_activity_at timestamp with time zone;

-- Add suspended_at to track when tenant was suspended (for 90-day deletion countdown)
ALTER TABLE public.tenants 
ADD COLUMN suspended_at timestamp with time zone;

-- Initialize last_activity_at to created_at for existing tenants
UPDATE public.tenants 
SET last_activity_at = COALESCE(updated_at, created_at)
WHERE last_activity_at IS NULL;

-- Make last_activity_at NOT NULL going forward with a default
ALTER TABLE public.tenants 
ALTER COLUMN last_activity_at SET DEFAULT now(),
ALTER COLUMN last_activity_at SET NOT NULL;

-- Add index for efficient dormancy queries
CREATE INDEX idx_tenants_last_activity ON public.tenants (last_activity_at) 
WHERE status = 'active';

CREATE INDEX idx_tenants_suspended_at ON public.tenants (suspended_at) 
WHERE status = 'suspended';

COMMENT ON COLUMN public.tenants.last_activity_at IS 'Last time any user accessed this tenant. Used for dormancy detection (30 days = suspend).';
COMMENT ON COLUMN public.tenants.suspended_at IS 'When tenant was suspended. Used for permanent deletion countdown (90 days after suspension).';
