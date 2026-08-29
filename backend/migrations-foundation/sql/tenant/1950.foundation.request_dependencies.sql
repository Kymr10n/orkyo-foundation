-- @migration-class: expand

-- Precedence edges between requests: "this one cannot start until that one finishes".
--
-- Deliberately a free-standing table over arbitrary request pairs rather than an ordering
-- column on requests. The tree (parent_request_id) expresses containment, this expresses
-- sequencing, and the two are independent: real precedence routinely crosses group and site
-- boundaries, so modelling it as sibling order inside a group would have to be undone later.
--
-- Only leaves may be linked. Summary and container rows carry rolled-up dates derived from
-- their descendants, so an edge on one has no executable meaning, and only leaves reach the
-- scheduler. That rule lives in the service layer rather than a CHECK here, because it reads
-- planning_mode from another table.
--
-- lag_minutes matches the canonical scheduling arithmetic (SchedulingEngine.DurationToMinutes);
-- the solver ceilings it to whole days at problem-build time exactly as it does durations, so
-- a lag never pulls a successor earlier than asked.
--
-- dependency_type is reserved. Only finish_to_start is accepted today and the CHECK says so;
-- start_to_start and the rest can be admitted later by widening the CHECK alone.
--
-- Cycles cannot be expressed as a constraint in Postgres. RequestDependencyService rejects
-- them with a recursive walk before insert, the same shape as the reparent cycle check.

CREATE TABLE public.request_dependencies (
    id                      uuid                     DEFAULT gen_random_uuid() NOT NULL,
    predecessor_request_id  uuid                     NOT NULL,
    successor_request_id    uuid                     NOT NULL,
    dependency_type         character varying(20)    NOT NULL DEFAULT 'finish_to_start',
    lag_minutes             integer                  NOT NULL DEFAULT 0,
    created_at              timestamp with time zone DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT request_dependencies_pkey PRIMARY KEY (id),
    CONSTRAINT request_dependencies_predecessor_fkey
        FOREIGN KEY (predecessor_request_id) REFERENCES public.requests(id) ON DELETE CASCADE,
    CONSTRAINT request_dependencies_successor_fkey
        FOREIGN KEY (successor_request_id) REFERENCES public.requests(id) ON DELETE CASCADE,
    CONSTRAINT request_dependencies_no_self
        CHECK (predecessor_request_id <> successor_request_id),
    CONSTRAINT request_dependencies_type_check
        CHECK (((dependency_type)::text = ANY ((ARRAY['finish_to_start'::character varying])::text[]))),
    CONSTRAINT request_dependencies_lag_nonnegative_check
        CHECK ((lag_minutes >= 0)),
    CONSTRAINT request_dependencies_edge_unique
        UNIQUE (predecessor_request_id, successor_request_id)
);

-- Only the successor side needs its own index: request_dependencies_edge_unique already gives a
-- btree led by predecessor_request_id, which serves the forward walks. GetBySuccessorsAsync is the
-- read that has nothing to lean on.
--
-- CONCURRENTLY cannot run inside a transaction. This script opens none, so the runner executes it
-- in autocommit and the index builds without holding a write lock.
CREATE INDEX CONCURRENTLY IF NOT EXISTS idx_request_dependencies_successor
    ON public.request_dependencies (successor_request_id);
