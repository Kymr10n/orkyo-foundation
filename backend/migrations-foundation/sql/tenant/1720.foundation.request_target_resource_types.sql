-- @migration-class: expand

-- Lets a request say what kinds of resource it needs.
--
-- Today a request cannot express this at all. "Scheduled" is defined as "has a non-cancelled
-- assignment to a resource whose type key is 'space'" — a rule written out nine times in the
-- repository and once more inside analytics_request_summary_v. So a request can only ever be
-- satisfied by a room, and a job that genuinely needs a room *and* a van *and* a technician
-- has no way to say so.
--
-- It cannot be inferred, either. Requirements (criteria) answer "which resource of the right
-- kind fits", not "what kind" — and migration 1470 tagged every untagged criterion with
-- 'space' to preserve historical behaviour, so inferring a type from requirements would
-- return 'space' for essentially everything in a migrated tenant. It has to be stored.
--
-- A join table rather than a column on `requests`: a request may need several types at once,
-- and each is satisfied independently by its own assignment.
--
-- This migration is inert on its own. Backfilling every existing request with 'space'
-- reproduces exactly today's rule, so the predicate that reads this table (next change)
-- returns what it returns now until someone adds a second type to a request.
--
-- Rollback: drop the table. Nothing else changes shape.

BEGIN;

CREATE TABLE IF NOT EXISTS public.request_target_resource_types (
    request_id       UUID NOT NULL REFERENCES public.requests(id)       ON DELETE CASCADE,
    resource_type_id UUID NOT NULL REFERENCES public.resource_types(id) ON DELETE RESTRICT,
    created_at       TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT request_target_resource_types_pkey PRIMARY KEY (request_id, resource_type_id)
);

COMMENT ON TABLE public.request_target_resource_types IS
    'The resource types a request needs, one row per type. A request is scheduled when every '
    'type listed here has a non-cancelled assignment. Empty means the request needs no '
    'resource and can never be scheduled.';

-- RESTRICT on the type: a type still named by a request must not be deletable out from under
-- it, which would silently turn "needs a van" into "needs nothing" and mark the request
-- scheduled. Deleting the request itself takes its targets with it.

-- The predicate joins from request to targets on every scheduled-request read, so the PK's
-- leading column already serves it. The reverse direction — "which requests want this type" —
-- is what the type-deletion check needs.
CREATE INDEX IF NOT EXISTS idx_request_target_types_type
    ON public.request_target_resource_types (resource_type_id);

-- ── Backfill ──────────────────────────────────────────────────────────────────
-- Every existing request targeted a space, because nothing else was possible: migration 1310
-- backfilled requests.space_id into resource_assignments, and the scheduled predicate has
-- only ever looked at spaces. Containers and summaries get a row too — they hold no
-- assignments, so they read as unscheduled either way, and giving them the same target keeps
-- the rule uniform rather than making planning_mode a second special case.
INSERT INTO public.request_target_resource_types (request_id, resource_type_id)
SELECT r.id, rt.id
  FROM public.requests r
 CROSS JOIN public.resource_types rt
 WHERE rt.key = 'space'
ON CONFLICT DO NOTHING;

-- ── Expose the targets on the read view ───────────────────────────────────────
-- DROP+CREATE rather than CREATE OR REPLACE, because the column list grows (precedent: 1550).
-- Only the repository reads this view.
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
    ) AS assignments
FROM requests r;

COMMIT;
