-- Calendar-aware scheduling: per-site settings, off-times, and per-space exclusions.
-- Verbatim from legacy V005 (the scheduling_settings_apply column it adds to requests
-- is folded into 1200.foundation.requests.sql).

CREATE TABLE public.scheduling_settings (
    id                       uuid                     DEFAULT gen_random_uuid() NOT NULL,
    site_id                  uuid                     NOT NULL,
    time_zone                character varying(100)   NOT NULL DEFAULT 'UTC',
    working_hours_enabled    boolean                  NOT NULL DEFAULT false,
    working_day_start        time                     NOT NULL DEFAULT '08:00',
    working_day_end          time                     NOT NULL DEFAULT '17:00',
    weekends_enabled         boolean                  NOT NULL DEFAULT true,
    public_holidays_enabled  boolean                  NOT NULL DEFAULT false,
    public_holiday_region    character varying(10),
    created_at               timestamp with time zone DEFAULT now() NOT NULL,
    updated_at               timestamp with time zone DEFAULT now() NOT NULL,

    CONSTRAINT scheduling_settings_pkey PRIMARY KEY (id),
    CONSTRAINT scheduling_settings_site_id_key UNIQUE (site_id),
    CONSTRAINT scheduling_settings_site_id_fkey
        FOREIGN KEY (site_id) REFERENCES public.sites(id) ON DELETE CASCADE,
    CONSTRAINT scheduling_settings_working_hours_order
        CHECK (working_day_start < working_day_end)
);

CREATE TRIGGER scheduling_settings_updated_at
    BEFORE UPDATE ON public.scheduling_settings
    FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();

CREATE TABLE public.off_times (
    id                       uuid                     DEFAULT gen_random_uuid() NOT NULL,
    site_id                  uuid                     NOT NULL,
    title                    character varying(200)   NOT NULL,
    type                     character varying(30)    NOT NULL DEFAULT 'custom',
    applies_to_all_spaces    boolean                  NOT NULL DEFAULT true,
    start_ts                 timestamp with time zone NOT NULL,
    end_ts                   timestamp with time zone NOT NULL,
    is_recurring             boolean                  NOT NULL DEFAULT false,
    recurrence_rule          character varying(500),
    enabled                  boolean                  NOT NULL DEFAULT true,
    created_at               timestamp with time zone DEFAULT now() NOT NULL,
    updated_at               timestamp with time zone DEFAULT now() NOT NULL,

    CONSTRAINT off_times_pkey PRIMARY KEY (id),
    CONSTRAINT off_times_site_id_fkey FOREIGN KEY (site_id) REFERENCES public.sites(id) ON DELETE CASCADE,
    CONSTRAINT off_times_type_check CHECK (type IN ('holiday', 'maintenance', 'custom')),
    CONSTRAINT off_times_time_range CHECK (end_ts > start_ts),
    CONSTRAINT off_times_recurrence_check CHECK (
        (is_recurring = false AND recurrence_rule IS NULL) OR
        (is_recurring = true AND recurrence_rule IS NOT NULL)
    )
);

CREATE INDEX idx_off_times_site_id    ON public.off_times (site_id);
CREATE INDEX idx_off_times_time_range ON public.off_times (site_id, start_ts, end_ts) WHERE enabled = true;

CREATE TRIGGER off_times_updated_at
    BEFORE UPDATE ON public.off_times
    FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();

CREATE TABLE public.off_time_spaces (
    off_time_id uuid NOT NULL,
    space_id    uuid NOT NULL,

    CONSTRAINT off_time_spaces_pkey PRIMARY KEY (off_time_id, space_id),
    CONSTRAINT off_time_spaces_off_time_fkey
        FOREIGN KEY (off_time_id) REFERENCES public.off_times(id) ON DELETE CASCADE,
    CONSTRAINT off_time_spaces_space_fkey
        FOREIGN KEY (space_id) REFERENCES public.spaces(id) ON DELETE CASCADE
);
