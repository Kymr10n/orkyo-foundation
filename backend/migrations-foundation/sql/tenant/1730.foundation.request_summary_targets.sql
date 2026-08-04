-- @migration-class: expand

-- Teaches the insights view the same "scheduled" rule the rest of the code now uses.
--
-- analytics_request_summary_v.is_scheduled hard-coded rt.key = 'space' (migration 1600). Now
-- that a request declares which resource types it needs, that test is wrong for any request
-- targeting anything else: the request would have both timestamps and a full set of
-- assignments and still report is_scheduled = false. Insights would show it as backlog
-- forever — silently, with no error, just a phantom backlog that grows.
--
-- The rule is now stated in three places that must agree: this view,
-- RequestRepository.FullyAssignedSql, and RequestInfo.IsScheduled. Same shape in each, so a
-- drift is visible when they are read side by side.
--
-- The EXISTS guard on the target list is load-bearing here too: without it a request
-- targeting nothing satisfies the inner NOT EXISTS vacuously and reports itself scheduled
-- while holding no resource at all.
--
-- Rollback: recreate the view from 1600.

BEGIN;

-- CREATE OR REPLACE would do for a predicate-only change, but the view is small and DROP
-- + CREATE keeps this file readable as the view's current definition (precedent: 1550).
DROP VIEW IF EXISTS analytics_request_summary_v;

CREATE VIEW analytics_request_summary_v AS
SELECT
    r.id                                          AS request_id,
    r.site_id,                                    -- NULL = site-neutral (schedulable anywhere)
    r.status,
    r.planning_mode,                              -- 'leaf' = schedulable unit; summary/container = grouping
    r.created_at,
    r.start_ts,                                   -- scheduled window start (NULL = backlog)
    r.end_ts,
    (
        r.start_ts IS NOT NULL
        AND r.end_ts IS NOT NULL
        AND EXISTS (
            SELECT 1 FROM request_target_resource_types t
             WHERE t.request_id = r.id
        )
        AND NOT EXISTS (
            SELECT 1
              FROM request_target_resource_types t
             WHERE t.request_id = r.id
               AND NOT EXISTS (
                   SELECT 1
                     FROM resource_assignments ra
                     JOIN resources res ON res.id = ra.resource_id
                    WHERE ra.request_id = r.id
                      AND res.resource_type_id = t.resource_type_id
                      AND ra.assignment_status <> 'Cancelled'
               )
        )
    )                                             AS is_scheduled,
    now()                                         AS calculated_at,
    'live'::text                                  AS source_mode
FROM requests r;

COMMIT;
