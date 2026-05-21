-- @migration-class: contract

-- Replace the conflated off_times / off_time_resources model with two clean
-- concepts:
--   * availability_events  — site-scoped operational events (holiday, shutdown,
--                            maintenance, custom) with a default effect and
--                            optional per-target overrides in
--                            availability_event_scopes.
--   * resource_absences    — resource-individual unavailability (vacation,
--                            sickness, training, repair). No site coupling.
--
-- Clean-break: the old tables are dropped without backfill. Paired revert lives
-- in backend/migrations-foundation/revert/1490.foundation.availability_model.revert.sql

-- Wrap everything in a single transaction so a failure during the DROPs
-- does not leave orphaned new tables while old ones still exist.
-- (The migrator does not add its own transaction wrapper.)
BEGIN;

-- ── availability_events ──────────────────────────────────────────────────────

CREATE TABLE public.availability_events (
    id                uuid                     DEFAULT gen_random_uuid() NOT NULL,
    site_id           uuid                     NOT NULL,
    title             character varying(200)   NOT NULL,
    description       text,
    event_type        character varying(30)    NOT NULL,
    default_effect    character varying(20)    NOT NULL,
    start_ts          timestamp with time zone NOT NULL,
    end_ts            timestamp with time zone NOT NULL,
    is_recurring      boolean                  NOT NULL DEFAULT false,
    recurrence_rule   character varying(500),
    enabled           boolean                  NOT NULL DEFAULT true,
    created_at        timestamp with time zone DEFAULT now() NOT NULL,
    updated_at        timestamp with time zone DEFAULT now() NOT NULL,

    CONSTRAINT availability_events_pkey PRIMARY KEY (id),
    CONSTRAINT availability_events_site_fkey
        FOREIGN KEY (site_id) REFERENCES public.sites(id) ON DELETE CASCADE,
    CONSTRAINT availability_events_event_type_check CHECK (
        event_type IN ('public_holiday', 'shutdown', 'maintenance', 'custom')
    ),
    CONSTRAINT availability_events_default_effect_check CHECK (
        default_effect IN ('closed', 'available')
    ),
    CONSTRAINT availability_events_time_range_check CHECK (end_ts > start_ts),
    CONSTRAINT availability_events_recurrence_check CHECK (
        (is_recurring = false AND recurrence_rule IS NULL) OR
        (is_recurring = true  AND recurrence_rule IS NOT NULL)
    )
);

CREATE INDEX idx_availability_events_site_id
    ON public.availability_events (site_id);
CREATE INDEX idx_availability_events_time_range
    ON public.availability_events (site_id, start_ts, end_ts)
    WHERE enabled = true;

CREATE TRIGGER availability_events_updated_at
    BEFORE UPDATE ON public.availability_events
    FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();

-- ── availability_event_scopes ────────────────────────────────────────────────
-- Polymorphic target_id is intentional: it resolves against resources,
-- resource_groups, or resource_types depending on target_type. Integrity is
-- enforced in the application layer (no cross-table FK).

CREATE TABLE public.availability_event_scopes (
    id                       uuid                  DEFAULT gen_random_uuid() NOT NULL,
    availability_event_id    uuid                  NOT NULL,
    target_type              character varying(20) NOT NULL,
    target_id                uuid                  NOT NULL,
    effect                   character varying(20) NOT NULL,

    CONSTRAINT availability_event_scopes_pkey PRIMARY KEY (id),
    CONSTRAINT availability_event_scopes_event_fkey
        FOREIGN KEY (availability_event_id)
        REFERENCES public.availability_events(id) ON DELETE CASCADE,
    CONSTRAINT availability_event_scopes_target_type_check CHECK (
        target_type IN ('resource', 'resource_group', 'resource_type')
    ),
    CONSTRAINT availability_event_scopes_effect_check CHECK (
        effect IN ('available', 'closed')
    ),
    CONSTRAINT availability_event_scopes_unique
        UNIQUE (availability_event_id, target_type, target_id)
);

CREATE INDEX idx_availability_event_scopes_target
    ON public.availability_event_scopes (target_type, target_id);

-- ── resource_absences ────────────────────────────────────────────────────────

CREATE TABLE public.resource_absences (
    id                uuid                     DEFAULT gen_random_uuid() NOT NULL,
    resource_id       uuid                     NOT NULL,
    absence_type      character varying(30)    NOT NULL,
    title             character varying(200)   NOT NULL,
    notes             text,
    start_ts          timestamp with time zone NOT NULL,
    end_ts            timestamp with time zone NOT NULL,
    is_recurring      boolean                  NOT NULL DEFAULT false,
    recurrence_rule   character varying(500),
    enabled           boolean                  NOT NULL DEFAULT true,
    created_at        timestamp with time zone DEFAULT now() NOT NULL,
    updated_at        timestamp with time zone DEFAULT now() NOT NULL,

    CONSTRAINT resource_absences_pkey PRIMARY KEY (id),
    CONSTRAINT resource_absences_resource_fkey
        FOREIGN KEY (resource_id) REFERENCES public.resources(id) ON DELETE CASCADE,
    CONSTRAINT resource_absences_type_check CHECK (
        absence_type IN ('vacation', 'sickness', 'unavailable', 'training', 'maintenance', 'custom')
    ),
    CONSTRAINT resource_absences_time_range_check CHECK (end_ts > start_ts),
    CONSTRAINT resource_absences_recurrence_check CHECK (
        (is_recurring = false AND recurrence_rule IS NULL) OR
        (is_recurring = true  AND recurrence_rule IS NOT NULL)
    )
);

CREATE INDEX idx_resource_absences_resource_id
    ON public.resource_absences (resource_id);
CREATE INDEX idx_resource_absences_time_range
    ON public.resource_absences (resource_id, start_ts, end_ts)
    WHERE enabled = true;

CREATE TRIGGER resource_absences_updated_at
    BEFORE UPDATE ON public.resource_absences
    FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();

-- ── Drop the legacy off_times model ──────────────────────────────────────────

DROP TABLE public.off_time_resources;
DROP TABLE public.off_times;

COMMIT;
