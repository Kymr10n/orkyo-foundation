-- @migration-class: expand

-- Add tenant_id to audit_events so a tenant admin can view their OWN tenant's audit
-- log (feature: audit_log_enabled, gated to Professional+ in SaaS; always on in Community).
-- Stamped by AdminAuditService from the resolved ICurrentTenant. Existing rows and
-- platform/site-admin events with no tenant context keep NULL and are excluded from
-- tenant-scoped views. The site-admin /api/admin/audit view is unaffected (reads all rows).

BEGIN;

ALTER TABLE public.audit_events ADD COLUMN IF NOT EXISTS tenant_id uuid;

-- Serves the tenant-scoped query: WHERE tenant_id = @t ORDER BY created_at DESC.
CREATE INDEX IF NOT EXISTS idx_audit_events_tenant_id
    ON public.audit_events (tenant_id, created_at DESC);

COMMIT;
