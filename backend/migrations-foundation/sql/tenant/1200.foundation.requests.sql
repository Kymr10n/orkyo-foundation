-- Requests — the core scheduled work-units in a tenant.
-- Folds: V005 (scheduling_settings_apply), V006 indexes, V007 indexes
-- (idx_requests_scheduling_join supersedes V006 idx_requests_scheduling, omitted),
-- V009 (parent_request_id, planning_mode, sort_order + tree FK + indexes).

CREATE TABLE public.requests (
    -- Column order matches legacy V001 + V005 + V009 (each ALTER appended at the end).
    id                        uuid                     DEFAULT gen_random_uuid() NOT NULL,
    name                      character varying(200)   NOT NULL,
    description               text,
    space_id                  uuid,
    request_item_id           character varying(100),
    start_ts                  timestamp with time zone,
    end_ts                    timestamp with time zone,
    minimal_duration_value    integer                  NOT NULL,
    minimal_duration_unit     character varying(20)    NOT NULL,
    status                    character varying(20)    DEFAULT 'planned'::character varying NOT NULL,
    created_at                timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    updated_at                timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    earliest_start_ts         timestamp with time zone,
    latest_end_ts             timestamp with time zone,
    actual_duration_value     integer,
    actual_duration_unit      character varying(20),

    -- V005
    scheduling_settings_apply boolean                  NOT NULL DEFAULT true,

    -- V009
    parent_request_id         uuid,
    planning_mode             character varying(20)    NOT NULL DEFAULT 'leaf',
    sort_order                integer                  NOT NULL DEFAULT 0,

    CONSTRAINT requests_pkey PRIMARY KEY (id),
    CONSTRAINT requests_space_id_fkey
        FOREIGN KEY (space_id) REFERENCES public.spaces(id) ON DELETE SET NULL,
    CONSTRAINT fk_requests_parent
        FOREIGN KEY (parent_request_id) REFERENCES public.requests(id) ON DELETE CASCADE,
    CONSTRAINT requests_no_self_parent
        CHECK (parent_request_id IS DISTINCT FROM id),
    CONSTRAINT requests_planning_mode_check
        CHECK (planning_mode IN ('leaf', 'summary', 'container')),
    CONSTRAINT requests_status_check
        CHECK (((status)::text = ANY ((ARRAY['planned'::character varying, 'in_progress'::character varying, 'done'::character varying, 'cancelled'::character varying])::text[]))),
    CONSTRAINT requests_minimal_duration_unit_check
        CHECK (((minimal_duration_unit)::text = ANY ((ARRAY['minutes'::character varying, 'hours'::character varying, 'days'::character varying, 'weeks'::character varying, 'months'::character varying, 'years'::character varying])::text[]))),
    CONSTRAINT requests_minimal_duration_value_check
        CHECK ((minimal_duration_value > 0)),
    CONSTRAINT requests_actual_duration_unit_check
        CHECK (((actual_duration_unit IS NULL) OR ((actual_duration_unit)::text = ANY ((ARRAY['minutes'::character varying, 'hours'::character varying, 'days'::character varying, 'weeks'::character varying, 'months'::character varying, 'years'::character varying])::text[])))),
    CONSTRAINT requests_actual_duration_value_check
        CHECK (((actual_duration_value IS NULL) OR (actual_duration_value > 0))),
    CONSTRAINT requests_actual_duration_complete_check
        CHECK ((((actual_duration_value IS NULL) AND (actual_duration_unit IS NULL)) OR ((actual_duration_value IS NOT NULL) AND (actual_duration_unit IS NOT NULL)))),
    CONSTRAINT requests_constraint_dates_order_check
        CHECK (((earliest_start_ts IS NULL) OR (latest_end_ts IS NULL) OR (earliest_start_ts < latest_end_ts))),
    CONSTRAINT requests_scheduled_within_constraints_check
        CHECK (((start_ts IS NULL) OR (earliest_start_ts IS NULL) OR (start_ts >= earliest_start_ts))),
    CONSTRAINT requests_scheduled_end_within_constraints_check
        CHECK (((end_ts IS NULL) OR (latest_end_ts IS NULL) OR (end_ts <= latest_end_ts))),
    CONSTRAINT valid_time_range
        CHECK ((((start_ts IS NULL) AND (end_ts IS NULL)) OR ((start_ts IS NOT NULL) AND (end_ts IS NOT NULL) AND (end_ts > start_ts))))
);

CREATE INDEX idx_requests_space_id         ON public.requests USING btree (space_id);
CREATE INDEX idx_requests_status           ON public.requests USING btree (status);
CREATE INDEX idx_requests_start_ts         ON public.requests USING btree (start_ts);
CREATE INDEX idx_requests_end_ts           ON public.requests USING btree (end_ts);
CREATE INDEX idx_requests_time_range       ON public.requests USING btree (start_ts, end_ts);
CREATE INDEX idx_requests_time_status      ON public.requests USING btree (start_ts, end_ts, status) WHERE ((start_ts IS NOT NULL) AND (end_ts IS NOT NULL));
CREATE INDEX idx_requests_status_created   ON public.requests (status, created_at DESC);
CREATE INDEX idx_requests_scheduling_join
    ON public.requests (space_id, scheduling_settings_apply, start_ts)
    WHERE scheduling_settings_apply = true AND start_ts IS NOT NULL;
CREATE INDEX idx_requests_parent_request_id ON public.requests (parent_request_id);
CREATE INDEX idx_requests_parent_sort       ON public.requests (parent_request_id, sort_order);

CREATE TRIGGER trigger_requests_updated_at
    BEFORE UPDATE ON public.requests
    FOR EACH ROW EXECUTE FUNCTION public.update_requests_updated_at();
