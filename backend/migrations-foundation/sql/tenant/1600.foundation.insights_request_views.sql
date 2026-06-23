-- @migration-class: expand
-- Built-in Insights analytics seam (live source). A stable, per-request projection that the
-- Insights service buckets at query time (date_trunc + generate_series) for the overview and
-- request-status trend. Conflict and utilization analytics are NOT views — that logic lives in
-- IConflictService / IUtilizationService and would drift if reimplemented in SQL.
--
-- No tenant_id column: tenant isolation is per-database (each tenant has its own DB; the
-- connection is already tenant-scoped). source_mode/calculated_at are part of the contract so the
-- live view can later be swapped for a snapshot-backed view without touching the API or UI.
--
-- "Scheduled" mirrors the domain definition (RequestInfo.IsScheduled): a time window AND a
-- non-cancelled space assignment — not merely start_ts being set.

CREATE VIEW analytics_request_summary_v AS
SELECT
    r.id                                          AS request_id,
    r.site_id,                                    -- NULL = site-neutral (schedulable anywhere)
    r.status,
    r.created_at,
    (
        r.start_ts IS NOT NULL
        AND r.end_ts IS NOT NULL
        AND EXISTS (
            SELECT 1
            FROM resource_assignments ra
            JOIN resources res     ON res.id = ra.resource_id
            JOIN resource_types rt ON rt.id  = res.resource_type_id
            WHERE ra.request_id = r.id
              AND rt.key = 'space'
              AND ra.assignment_status <> 'Cancelled'
        )
    )                                             AS is_scheduled,
    now()                                         AS calculated_at,
    'live'::text                                  AS source_mode
FROM requests r;
