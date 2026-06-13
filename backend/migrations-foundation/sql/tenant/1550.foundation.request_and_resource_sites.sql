-- @migration-class: expand
-- Home-Site / Current-Site model: give requests a first-class (nullable) site and
-- give resources a home/current site + cross-site flag. Spaces keep using
-- spaces.site_id (immovable); the new resource columns stay NULL for spaces.
-- Execution site is derived (space site -> request site -> none), not stored.

-- ── requests: nullable site scope (NULL = site-neutral, schedulable anywhere) ──
ALTER TABLE requests
  ADD COLUMN site_id UUID NULL REFERENCES sites(id);

CREATE INDEX idx_requests_site_id ON requests (site_id);

-- ── resources: home/current site + cross-site availability ────────────────────
-- Generic columns so people, tools, and future resource types share one model.
-- Spaces leave these NULL and continue to resolve their site via spaces.site_id.
ALTER TABLE resources
  ADD COLUMN home_site_id      UUID    NULL REFERENCES sites(id),
  ADD COLUMN current_site_id   UUID    NULL REFERENCES sites(id),
  ADD COLUMN cross_site_allowed BOOLEAN NOT NULL DEFAULT true;

CREATE INDEX idx_resources_current_site ON resources (current_site_id);

-- Spaces are immovable: their location is spaces.site_id and they are never
-- cross-site. Mark the flag false so a stray default `true` can't mislead.
UPDATE resources
SET cross_site_allowed = false
WHERE resource_type_id IN (SELECT id FROM resource_types WHERE key = 'space');

-- Backfill for single-site tenants: a non-space resource's home/current site is
-- unambiguous. Multi-site tenants are left NULL for admin remediation (no guess).
UPDATE resources
SET home_site_id    = (SELECT id FROM sites LIMIT 1),
    current_site_id = (SELECT id FROM sites LIMIT 1)
WHERE (SELECT count(*) FROM sites) = 1
  AND resource_type_id IN (SELECT id FROM resource_types WHERE key <> 'space');

-- Requests: a single-site tenant has an unambiguous site, so scope every existing request to it.
-- This keeps the free / single-site tier free of the site concept entirely — every request "just
-- belongs" to the one site and shows on its calendar, with no neutral-limbo. Multi-site tenants stay
-- NULL (site-neutral): a historical request's intended site is unknown and its execution site is
-- implied by the scheduled space.
UPDATE requests
SET site_id = (SELECT id FROM sites LIMIT 1)
WHERE (SELECT count(*) FROM sites) = 1
  AND site_id IS NULL;

-- ── recompose the read view to expose requests.site_id ────────────────────────
-- DROP+CREATE (not CREATE OR REPLACE) so site_id can sit with the other request
-- columns; nothing else depends on this view (only the repository reads it).
DROP VIEW v_requests_with_assignments;

CREATE VIEW v_requests_with_assignments AS
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
    ) AS assignments
FROM requests r;
