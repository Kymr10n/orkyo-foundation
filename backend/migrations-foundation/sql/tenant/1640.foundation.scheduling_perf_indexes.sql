-- @migration-class: expand

-- resource_group_members: the PK is (resource_group_id, resource_id), which only
-- accelerates lookups leading with resource_group_id. Queries that filter by
-- resource_id alone (e.g. GetGroupIdsForResource(s)Async in the availability
-- resolver) need a dedicated index.
CREATE INDEX CONCURRENTLY idx_rgm_resource_id
    ON public.resource_group_members (resource_id);

-- resource_assignments: idx_ra_conflict leads with resource_id, so time-window
-- scans that are NOT anchored to a specific resource (utilization/insights
-- aggregates over an interval) fall back to a sequential scan. Same partial
-- predicate as idx_ra_conflict (migration 1300) — cancelled rows never
-- participate in occupancy queries.
CREATE INDEX CONCURRENTLY idx_ra_time_range
    ON public.resource_assignments (start_utc, end_utc)
    WHERE assignment_status != 'Cancelled';
