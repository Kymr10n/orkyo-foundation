-- @migration-class: expand

-- Lets a task say WHICH of its predecessors have to be met before it may start.
--
-- Dependencies already exist (1950), but their semantics are implicitly "wait for every
-- predecessor": each of the three readers — the critical path, the auto-schedule problem
-- builder and the conflict detector — folds incoming edges with a max, which is an AND. A
-- plan that says "start once EITHER supplier has delivered", or "once any two of these three
-- inspections pass", cannot be expressed at all.
--
-- The condition belongs to the SUCCESSOR, not to the edge: it is a property of a node's whole
-- incoming set ("I need 2 of my 3"), and storing it per edge would let a set disagree with
-- itself. So it lands on `requests` rather than widening `request_dependencies`.
--
-- ── k may exceed the number of edges, on purpose ──────────────────────────────
-- Edges are added and removed independently of this column, so a CHECK tying k to the live
-- edge count is not expressible and would make ordinary edge deletion fail. k is therefore
-- only shape-checked here (>= 1, present exactly for k_of_n) and CLAMPED at evaluation to
-- min(k, live predecessors). A stored 5 on a node with 3 predecessors reads as "all 3".
--
-- ── Cancelled and deferred predecessors leave the set ─────────────────────────
-- Evaluation drops predecessors whose effective status is cancelled or deferred before
-- counting, so n shrinks and k clamps with it. Without that rule a cancelled predecessor
-- would hold an "all" join shut forever, with no way to proceed but to delete the edge and
-- lose the record that it existed. An empty live set counts as met.
--
-- Inert until something sets a value: every existing row defaults to 'all', which is exactly
-- what the three readers already do today.
--
-- Rollback: drop the two columns and re-create the view without them.

BEGIN;

ALTER TABLE public.requests
    ADD COLUMN predecessor_logic   varchar(10) NOT NULL DEFAULT 'all',
    ADD COLUMN predecessor_logic_k integer;

-- NOT VALID, then validated after COMMIT. Adding the columns is metadata-only (a non-volatile
-- default, PG11+), but a validating CHECK scans the whole table under ACCESS EXCLUSIVE — and
-- `requests` is the busiest table here. NOT VALID takes the lock only long enough to record the
-- constraint; VALIDATE below then scans under a lock that permits reads and writes. Every
-- existing row satisfies both by construction (the column default is 'all' and k is NULL), so
-- the validation cannot fail.
ALTER TABLE public.requests
    ADD CONSTRAINT requests_predecessor_logic_check
        CHECK (predecessor_logic IN ('all', 'any', 'k_of_n')) NOT VALID;

-- k is meaningful only for k_of_n, and meaningless without it. Enforcing both directions
-- keeps the pair from drifting into a state no reader knows how to interpret.
ALTER TABLE public.requests
    ADD CONSTRAINT requests_predecessor_logic_k_check
        CHECK ((predecessor_logic =  'k_of_n' AND predecessor_logic_k >= 1)
            OR (predecessor_logic <> 'k_of_n' AND predecessor_logic_k IS NULL)) NOT VALID;

COMMENT ON COLUMN public.requests.predecessor_logic IS
    'How this request joins its incoming dependencies: all (default), any, or k_of_n. '
    'Cancelled and deferred predecessors are excluded before the condition is evaluated.';
COMMENT ON COLUMN public.requests.predecessor_logic_k IS
    'The k of k_of_n; NULL for the other logics. Clamped to the live predecessor count at '
    'evaluation, so a value larger than the number of edges reads as "all of them".';

-- ── Expose the columns on the read view ───────────────────────────────────────
-- DROP+CREATE rather than CREATE OR REPLACE, because the column list grows (precedent: 1720).
-- Appended at the END of the list to keep the diff against 1720 readable; the repository maps
-- this view by column NAME, so position is not load-bearing.
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
    ) AS assignments,
    r.predecessor_logic, r.predecessor_logic_k
FROM requests r;

COMMIT;

-- After COMMIT so the scans run outside the transaction that took ACCESS EXCLUSIVE above. The
-- runner does not wrap scripts, so statements after COMMIT run in autocommit (precedent: 1720's
-- CREATE INDEX CONCURRENTLY).
ALTER TABLE public.requests VALIDATE CONSTRAINT requests_predecessor_logic_check;
ALTER TABLE public.requests VALIDATE CONSTRAINT requests_predecessor_logic_k_check;
