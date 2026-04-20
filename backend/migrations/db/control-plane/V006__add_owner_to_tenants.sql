-- Add owner_user_id to tenants table
-- Tracks who created/owns the tenant for the "one tenant per user" policy

ALTER TABLE public.tenants
ADD COLUMN owner_user_id uuid REFERENCES public.users(id) ON DELETE SET NULL;

-- Index for looking up tenants by owner
CREATE INDEX idx_tenants_owner_user_id ON public.tenants (owner_user_id);

COMMENT ON COLUMN public.tenants.owner_user_id IS 'User who created/owns this tenant. Used for one-tenant-per-user policy.';

-- Backfill existing tenants: set owner to first admin member
UPDATE public.tenants t
SET owner_user_id = (
    SELECT tm.user_id
    FROM public.tenant_memberships tm
    WHERE tm.tenant_id = t.id
      AND tm.role = 'admin'
      AND tm.status = 'active'
    ORDER BY tm.created_at ASC
    LIMIT 1
)
WHERE t.owner_user_id IS NULL;
