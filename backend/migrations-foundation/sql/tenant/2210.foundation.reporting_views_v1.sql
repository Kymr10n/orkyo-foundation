-- @migration-class: expand
-- Reporting views for the three MVP embedded BI reports.
--
-- All views live in the 'reporting' schema created by 2200.
-- The rpt_reader role accesses data only through these views; it has no
-- SELECT on any public.* table directly (PostgreSQL uses the view owner's
-- privileges to access the underlying tables, not the querying role's).
--
-- Views are intentionally narrow: no audit columns, no internal-only UUIDs
-- beyond those needed for drill-down, no PII beyond display names.

-- ── rpt_space_utilization ────────────────────────────────────────────────────
-- One row per non-cancelled resource_assignment for resources of type 'space'.
-- Date-trunc columns are pre-computed to serve as Superset time grain pivots.
CREATE VIEW reporting.rpt_space_utilization AS
SELECT
    ra.id                                                       AS assignment_id,
    ra.request_id,
    r.name                                                      AS request_name,
    r.status                                                    AS request_status,
    ra.resource_id                                              AS space_id,
    s.code                                                      AS space_code,
    res.name                                                    AS space_name,
    s.capacity                                                  AS space_capacity,
    s.site_id,
    si.name                                                     AS site_name,
    ra.start_utc,
    ra.end_utc,
    EXTRACT(EPOCH FROM (ra.end_utc - ra.start_utc)) / 3600.0   AS duration_hours,
    ra.allocation_percent,
    ra.assignment_status,
    date_trunc('day',   ra.start_utc AT TIME ZONE 'UTC')       AS day,
    date_trunc('week',  ra.start_utc AT TIME ZONE 'UTC')       AS iso_week,
    date_trunc('month', ra.start_utc AT TIME ZONE 'UTC')       AS month
FROM resource_assignments ra
JOIN resources             res ON res.id             = ra.resource_id
JOIN resource_types        rt  ON rt.id              = res.resource_type_id
                               AND rt.key            = 'space'
JOIN spaces                s   ON s.id               = ra.resource_id
JOIN requests              r   ON r.id               = ra.request_id
JOIN sites                 si  ON si.id              = s.site_id
WHERE ra.assignment_status != 'Cancelled';

-- ── rpt_request_pipeline ─────────────────────────────────────────────────────
-- One row per leaf request. Shows status distribution and scheduling throughput
-- over time. Summary and container requests are excluded — they carry no
-- independent scheduling data and would inflate counts.
CREATE VIEW reporting.rpt_request_pipeline AS
SELECT
    r.id                                                        AS request_id,
    r.name,
    r.status,
    r.planning_mode,
    r.start_ts,
    r.end_ts,
    r.created_at,
    r.updated_at,
    CASE
        WHEN r.start_ts IS NOT NULL AND r.end_ts IS NOT NULL
        THEN EXTRACT(EPOCH FROM (r.end_ts - r.start_ts)) / 3600.0
    END                                                         AS scheduled_hours,
    date_trunc('day',   r.created_at AT TIME ZONE 'UTC')       AS created_day,
    date_trunc('week',  r.created_at AT TIME ZONE 'UTC')       AS created_week,
    date_trunc('month', r.created_at AT TIME ZONE 'UTC')       AS created_month,
    date_trunc('day',   r.start_ts   AT TIME ZONE 'UTC')       AS scheduled_day,
    date_trunc('month', r.start_ts   AT TIME ZONE 'UTC')       AS scheduled_month
FROM requests r
WHERE r.planning_mode = 'leaf';

-- ── rpt_allocation_conflicts ─────────────────────────────────────────────────
-- Pairs of time-overlapping non-cancelled assignments for the same Exclusive
-- resource. Fractional and ConcurrentCapacity resources are excluded — their
-- over-allocation model requires different analysis.
-- Each conflict pair appears once: the canonical ordering ra1.id < ra2.id
-- prevents double-counting.
CREATE VIEW reporting.rpt_allocation_conflicts AS
SELECT
    ra1.resource_id,
    res.name                                                    AS resource_name,
    rt.key                                                      AS resource_type_key,
    ra1.id                                                      AS assignment_a_id,
    ra1.request_id                                              AS request_a_id,
    ra2.id                                                      AS assignment_b_id,
    ra2.request_id                                              AS request_b_id,
    GREATEST(ra1.start_utc, ra2.start_utc)                     AS overlap_start,
    LEAST(ra1.end_utc,   ra2.end_utc)                          AS overlap_end,
    EXTRACT(EPOCH FROM (
        LEAST(ra1.end_utc,   ra2.end_utc) -
        GREATEST(ra1.start_utc, ra2.start_utc)
    )) / 3600.0                                                 AS overlap_hours
FROM resource_assignments ra1
JOIN resource_assignments  ra2  ON  ra2.resource_id       = ra1.resource_id
                                AND ra2.id                 > ra1.id
                                AND ra2.start_utc          < ra1.end_utc
                                AND ra2.end_utc            > ra1.start_utc
                                AND ra2.assignment_status != 'Cancelled'
JOIN resources             res  ON  res.id                = ra1.resource_id
JOIN resource_types        rt   ON  rt.id                 = res.resource_type_id
WHERE ra1.assignment_status != 'Cancelled'
  AND res.allocation_mode   = 'Exclusive';

-- ── Grant SELECT on all reporting views to rpt_reader ────────────────────────
DO $$
DECLARE
    _role text := current_database() || '_rpt_reader';
BEGIN
    -- Grant on views that exist now.
    EXECUTE format('GRANT SELECT ON ALL TABLES IN SCHEMA reporting TO %I', _role);

    -- Ensure any views added in future reporting migrations are auto-granted.
    EXECUTE format(
        'ALTER DEFAULT PRIVILEGES IN SCHEMA reporting GRANT SELECT ON TABLES TO %I',
        _role
    );
END
$$;
