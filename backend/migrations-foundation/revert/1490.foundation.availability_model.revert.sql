-- Revert migration 1490 (availability_events / resource_absences model).
-- Recreates the legacy off_times / off_time_resources tables in the shape they
-- had after migrations 1270 → 1310 → 1480. Data is NOT restored — clean break.

CREATE TABLE public.off_times (
    id                        uuid                     DEFAULT gen_random_uuid() NOT NULL,
    site_id                   uuid                     NOT NULL,
    title                     character varying(200)   NOT NULL,
    type                      character varying(30)    NOT NULL DEFAULT 'custom',
    applies_to_all_resources  boolean                  NOT NULL DEFAULT true,
    start_ts                  timestamp with time zone NOT NULL,
    end_ts                    timestamp with time zone NOT NULL,
    is_recurring              boolean                  NOT NULL DEFAULT false,
    recurrence_rule           character varying(500),
    enabled                   boolean                  NOT NULL DEFAULT true,
    created_at                timestamp with time zone DEFAULT now() NOT NULL,
    updated_at                timestamp with time zone DEFAULT now() NOT NULL,

    CONSTRAINT off_times_pkey PRIMARY KEY (id),
    CONSTRAINT off_times_site_id_fkey
        FOREIGN KEY (site_id) REFERENCES public.sites(id) ON DELETE CASCADE,
    CONSTRAINT off_times_type_check CHECK (
        type IN ('holiday', 'maintenance', 'custom', 'vacation', 'sick_leave', 'unavailable', 'training')
    ),
    CONSTRAINT off_times_time_range CHECK (end_ts > start_ts),
    CONSTRAINT off_times_recurrence_check CHECK (
        (is_recurring = false AND recurrence_rule IS NULL) OR
        (is_recurring = true  AND recurrence_rule IS NOT NULL)
    )
);

CREATE INDEX idx_off_times_site_id    ON public.off_times (site_id);
CREATE INDEX idx_off_times_time_range ON public.off_times (site_id, start_ts, end_ts) WHERE enabled = true;

CREATE TRIGGER off_times_updated_at
    BEFORE UPDATE ON public.off_times
    FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();

CREATE TABLE public.off_time_resources (
    off_time_id uuid NOT NULL,
    resource_id uuid NOT NULL,

    CONSTRAINT off_time_resources_pkey PRIMARY KEY (off_time_id, resource_id),
    CONSTRAINT off_time_resources_off_time_fkey
        FOREIGN KEY (off_time_id) REFERENCES public.off_times(id) ON DELETE CASCADE,
    CONSTRAINT off_time_resources_resource_fkey
        FOREIGN KEY (resource_id) REFERENCES public.resources(id) ON DELETE CASCADE
);

DROP TABLE public.resource_absences;
DROP TABLE public.availability_event_scopes;
DROP TABLE public.availability_events;
