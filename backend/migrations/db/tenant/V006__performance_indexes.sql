-- Performance indexes identified by audit (2026-04-03)

-- Composite index for audit queries filtering by target entity
CREATE INDEX idx_audit_events_target ON public.audit_events (target_type, target_id);

-- FK index for user-scoped feedback queries
CREATE INDEX idx_feedback_user_id ON public.feedback (user_id);

-- Sorted request listings by status
CREATE INDEX idx_requests_status_created ON public.requests (status, created_at DESC);

-- Scheduling recalculation: find requests with scheduling enabled, joined to spaces
CREATE INDEX idx_requests_scheduling ON public.requests (space_id, scheduling_settings_apply)
    WHERE scheduling_settings_apply = true AND space_id IS NOT NULL AND start_ts IS NOT NULL;
