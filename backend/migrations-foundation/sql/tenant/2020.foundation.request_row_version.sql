-- @migration-class: expand

-- Exposes the row version of a request on the read view, for optimistic concurrency (#299).
--
-- The version is the Postgres system column xmin. Every update of the row gives it a new
-- value, so it needs no new column, trigger or backfill. A client reads `version`, sends it
-- back as If-Match on a write, and the write fails with 412 when the row changed meanwhile.
--
-- CREATE OR REPLACE, not DROP+CREATE: the new column is appended at the end, which REPLACE
-- allows, and the view never disappears while a deploy runs. The rest of the definition is
-- 1960's, unchanged.

-- Only the repository reads this view.
BEGIN;

CREATE OR REPLACE VIEW v_requests_with_assignments AS
SELECT
    r.id, r.name, r.description,
    r.parent_request_id, r.planning_mode, r.sort_order,
    r.site_id,
    r.request_item_id, r.icon,
    r.start_ts, r.end_ts, r.earliest_start_ts, r.latest_end_ts,
    r.minimal_duration_value, r.minimal_duration_unit,
    r.actual_duration_value, r.actual_duration_unit,
    r.status, r.scheduling_settings_apply,
    r.created_at, r.updated_at,
    -- The resource types this request needs, sorted for snapshot stability. Aggregated as a
    -- plain text array rather than JSONB: it is a set of keys, with nothing else to carry.
    COALESCE(
      (SELECT array_agg(rt.key ORDER BY rt.key)
         FROM request_target_resource_types trt
         JOIN resource_types rt ON rt.id = trt.resource_type_id
        WHERE trt.request_id = r.id),
      ARRAY[]::text[]
    ) AS target_resource_type_keys,
    -- Assignments aggregated as JSONB. Ordered by (rt.key, ra.start_utc) for
    -- snapshot-test stability; consumers may rely on this ordering.
    -- Cancelled assignments are excluded at the source so callers cannot
    -- accidentally include them.
    COALESCE(
      (SELECT jsonb_agg(jsonb_build_object(
          'id',                 ra.id,
          'request_id',         ra.request_id,
          'resource_id',        ra.resource_id,
          'resource_type_key',  rt.key,
          'start_utc',          ra.start_utc,
          'end_utc',            ra.end_utc,
          'allocation_percent', ra.allocation_percent,
          'allocation_units',   ra.allocation_units,
          'assignment_status',  ra.assignment_status,
          'created_at',         ra.created_at,
          'updated_at',         ra.updated_at
        ) ORDER BY rt.key, ra.start_utc)
       FROM resource_assignments ra
       JOIN resources res     ON res.id = ra.resource_id
       JOIN resource_types rt ON rt.id  = res.resource_type_id
       WHERE ra.request_id = r.id
         AND ra.assignment_status != 'Cancelled'),
      '[]'::jsonb
    ) AS assignments,
    r.predecessor_logic, r.predecessor_logic_k,
    r.xmin AS row_version
FROM requests r;

COMMIT;
