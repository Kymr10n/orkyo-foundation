-- @migration-class: expand

-- Routings: a part's sequence of operations, kept once and instantiated per work order.
--
-- A shop that runs the same bracket forty times a year cannot draw the same chain of
-- dependencies forty times by hand. A routing is that chain as data: an ordered list of
-- steps, each naming an operation and how long it takes. Instantiating it creates one
-- container request for the job, one leaf request per step, and the finish-to-start edges
-- between consecutive steps — all in one transaction, from RoutingService.
--
-- An operation is an existing request template (entity_type = 'request'). That is where its
-- target resource types (migration 1970) and criterion requirements already live, so a
-- routing step stores only what is specific to this part: setup time, run time per unit,
-- and the lag before the next step. Duration at instantiation is
-- setup_minutes + run_minutes_per_unit * quantity, written in minutes. Quantity is an input
-- to the instantiation, not a column: the durations it produces are what the plan needs.
--
-- RESTRICT on the operation template: deleting an operation that a routing still uses would
-- silently turn a five-step part into a four-step one. Deleting the routing takes its steps.
--
-- Rollback: drop both tables. Instantiated requests are ordinary requests and stay.

CREATE TABLE public.routings (
    id          uuid                     DEFAULT gen_random_uuid() NOT NULL,
    name        character varying(255)   NOT NULL,
    description text,
    created_at  timestamp with time zone DEFAULT now() NOT NULL,
    updated_at  timestamp with time zone DEFAULT now() NOT NULL,

    CONSTRAINT routings_pkey PRIMARY KEY (id)
);

CREATE TRIGGER update_routings_updated_at
    BEFORE UPDATE ON public.routings
    FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();

CREATE TABLE public.routing_steps (
    id                    uuid    DEFAULT gen_random_uuid() NOT NULL,
    routing_id            uuid    NOT NULL,
    step_no               integer NOT NULL,
    operation_template_id uuid    NOT NULL,
    setup_minutes         integer NOT NULL DEFAULT 0,
    run_minutes_per_unit  integer NOT NULL DEFAULT 0,
    lag_minutes_after     integer NOT NULL DEFAULT 0,

    CONSTRAINT routing_steps_pkey PRIMARY KEY (id),
    CONSTRAINT routing_steps_routing_fkey
        FOREIGN KEY (routing_id) REFERENCES public.routings(id) ON DELETE CASCADE,
    CONSTRAINT routing_steps_template_fkey
        FOREIGN KEY (operation_template_id) REFERENCES public.templates(id) ON DELETE RESTRICT,
    CONSTRAINT routing_steps_step_no_positive CHECK (step_no > 0),
    CONSTRAINT routing_steps_setup_nonnegative CHECK (setup_minutes >= 0),
    CONSTRAINT routing_steps_run_nonnegative CHECK (run_minutes_per_unit >= 0),
    CONSTRAINT routing_steps_lag_nonnegative CHECK (lag_minutes_after >= 0),
    -- A step that takes no time for any quantity would create a zero-length request.
    CONSTRAINT routing_steps_takes_time CHECK (setup_minutes + run_minutes_per_unit > 0),
    CONSTRAINT routing_steps_step_no_unique UNIQUE (routing_id, step_no)
);

COMMENT ON TABLE public.routing_steps IS
    'One operation of a routing. Instantiated as a leaf request lasting '
    'setup_minutes + run_minutes_per_unit * quantity, followed by a finish-to-start edge to '
    'the next step with lag_minutes_after.';

-- The RESTRICT lookup on template delete, and "which routings use this operation".
-- The runner does not wrap scripts, so CONCURRENTLY is legal here.
CREATE INDEX CONCURRENTLY idx_routing_steps_template
    ON public.routing_steps (operation_template_id);
