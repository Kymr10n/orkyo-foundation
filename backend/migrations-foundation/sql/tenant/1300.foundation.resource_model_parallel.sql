-- Resource model — parallel build (Phase 1, additive only).
-- No existing tables are altered or dropped in this migration.
-- Phase 2 will rename and wire these tables into production code paths.

-- ── resource_types ────────────────────────────────────────────────────────────

CREATE TABLE public.resource_types (
    id           UUID                     NOT NULL DEFAULT gen_random_uuid(),
    key          VARCHAR(50)              NOT NULL,
    display_name VARCHAR(100)             NOT NULL,
    description  TEXT,
    is_system    BOOLEAN                  NOT NULL DEFAULT false,
    is_active    BOOLEAN                  NOT NULL DEFAULT true,
    created_at   TIMESTAMPTZ              NOT NULL DEFAULT now(),
    updated_at   TIMESTAMPTZ              NOT NULL DEFAULT now(),

    CONSTRAINT resource_types_pkey        PRIMARY KEY (id),
    CONSTRAINT resource_types_key_unique  UNIQUE (key)
);

CREATE INDEX idx_resource_types_active ON public.resource_types (is_active) WHERE is_active;

CREATE TRIGGER resource_types_updated_at
    BEFORE UPDATE ON public.resource_types
    FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();

INSERT INTO public.resource_types (key, display_name, is_system, is_active)
VALUES
    ('space',  'Space',  true, true),
    ('person', 'Person', true, true),
    ('tool',   'Tool',   true, true);

-- ── resources ─────────────────────────────────────────────────────────────────

CREATE TABLE public.resources (
    id                        UUID        NOT NULL DEFAULT gen_random_uuid(),
    resource_type_id          UUID        NOT NULL,
    name                      VARCHAR(255) NOT NULL,
    description               TEXT,
    external_reference        VARCHAR(255),
    allocation_mode           VARCHAR(30) NOT NULL
        CONSTRAINT resources_allocation_mode_check
            CHECK (allocation_mode IN ('Exclusive','Fractional','ConcurrentCapacity')),
    base_availability_percent INT         NOT NULL DEFAULT 100
        CONSTRAINT resources_availability_pct_check
            CHECK (base_availability_percent BETWEEN 0 AND 100),
    is_active                 BOOLEAN     NOT NULL DEFAULT true,
    metadata_json             JSONB,
    created_at                TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at                TIMESTAMPTZ NOT NULL DEFAULT now(),

    CONSTRAINT resources_pkey PRIMARY KEY (id),
    CONSTRAINT resources_resource_type_fkey
        FOREIGN KEY (resource_type_id) REFERENCES public.resource_types(id)
);

CREATE INDEX idx_resources_type   ON public.resources (resource_type_id);
CREATE INDEX idx_resources_active ON public.resources (is_active) WHERE is_active;

CREATE TRIGGER resources_updated_at
    BEFORE UPDATE ON public.resources
    FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();

-- ── resource_assignments ──────────────────────────────────────────────────────

CREATE TABLE public.resource_assignments (
    id                 UUID        NOT NULL DEFAULT gen_random_uuid(),
    request_id         UUID        NOT NULL,
    resource_id        UUID        NOT NULL,
    start_utc          TIMESTAMPTZ NOT NULL,
    end_utc            TIMESTAMPTZ NOT NULL,
    allocation_percent NUMERIC(5,2),
    allocation_units   INT,
    assignment_status  VARCHAR(20) NOT NULL DEFAULT 'Planned'
        CONSTRAINT resource_assignments_status_check
            CHECK (assignment_status IN ('Planned','Confirmed','Tentative','Cancelled')),
    created_at         TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at         TIMESTAMPTZ NOT NULL DEFAULT now(),

    CONSTRAINT resource_assignments_pkey PRIMARY KEY (id),
    CONSTRAINT resource_assignments_request_fkey
        FOREIGN KEY (request_id)  REFERENCES public.requests(id)   ON DELETE CASCADE,
    CONSTRAINT resource_assignments_resource_fkey
        FOREIGN KEY (resource_id) REFERENCES public.resources(id)  ON DELETE RESTRICT,
    CONSTRAINT resource_assignments_time_range
        CHECK (end_utc > start_utc),
    CONSTRAINT resource_assignments_alloc_pct_range
        CHECK (allocation_percent IS NULL OR (allocation_percent > 0 AND allocation_percent <= 100))
);

CREATE INDEX idx_ra_conflict ON public.resource_assignments (resource_id, start_utc, end_utc)
    WHERE assignment_status != 'Cancelled';

CREATE INDEX idx_ra_request ON public.resource_assignments (request_id)
    WHERE assignment_status != 'Cancelled';

CREATE UNIQUE INDEX ux_ra_active_request_resource ON public.resource_assignments (request_id, resource_id)
    WHERE assignment_status != 'Cancelled';

CREATE TRIGGER resource_assignments_updated_at
    BEFORE UPDATE ON public.resource_assignments
    FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();

-- ── criterion_resource_types ──────────────────────────────────────────────────
-- Tags each criterion with the resource types it may be assigned to.

CREATE TABLE public.criterion_resource_types (
    criterion_id     UUID NOT NULL,
    resource_type_id UUID NOT NULL,

    CONSTRAINT criterion_resource_types_pkey
        PRIMARY KEY (criterion_id, resource_type_id),
    CONSTRAINT criterion_resource_types_criterion_fkey
        FOREIGN KEY (criterion_id)     REFERENCES public.criteria(id)        ON DELETE CASCADE,
    CONSTRAINT criterion_resource_types_type_fkey
        FOREIGN KEY (resource_type_id) REFERENCES public.resource_types(id)  ON DELETE CASCADE
);

-- Backfill: every existing criterion is applicable to type=space.
INSERT INTO public.criterion_resource_types (criterion_id, resource_type_id)
SELECT c.id, rt.id
FROM   public.criteria c
       CROSS JOIN public.resource_types rt
WHERE  rt.key = 'space'
ON CONFLICT DO NOTHING;

-- ── resource_capabilities_phase1 (staging) ────────────────────────────────────
-- Parallel staging table for the new resource model. Renamed to
-- resource_capabilities in Phase 2 after space_capabilities is migrated.

CREATE TABLE public.resource_capabilities_phase1 (
    id           UUID        NOT NULL DEFAULT gen_random_uuid(),
    resource_id  UUID        NOT NULL,
    criterion_id UUID        NOT NULL,
    value        JSONB       NOT NULL,
    created_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at   TIMESTAMPTZ NOT NULL DEFAULT now(),

    CONSTRAINT resource_capabilities_phase1_pkey
        PRIMARY KEY (id),
    CONSTRAINT resource_capabilities_phase1_unique
        UNIQUE (resource_id, criterion_id),
    CONSTRAINT resource_capabilities_phase1_resource_fkey
        FOREIGN KEY (resource_id)  REFERENCES public.resources(id)  ON DELETE CASCADE,
    CONSTRAINT resource_capabilities_phase1_criterion_fkey
        FOREIGN KEY (criterion_id) REFERENCES public.criteria(id)   ON DELETE CASCADE
);

CREATE INDEX idx_rc_phase1_resource  ON public.resource_capabilities_phase1 (resource_id);
CREATE INDEX idx_rc_phase1_criterion ON public.resource_capabilities_phase1 (criterion_id);

CREATE TRIGGER resource_capabilities_phase1_updated_at
    BEFORE UPDATE ON public.resource_capabilities_phase1
    FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();

-- ── criteria_applicability_phase1 (staging) ───────────────────────────────────
-- Per-criterion flag controlling whether it may be used as a Request requirement.
-- Merged into criteria.applicable_to_requests (Phase 3 migration adds that column).

CREATE TABLE public.criteria_applicability_phase1 (
    criterion_id          UUID    NOT NULL,
    applicable_to_requests BOOLEAN NOT NULL DEFAULT true,

    CONSTRAINT criteria_applicability_phase1_pkey
        PRIMARY KEY (criterion_id),
    CONSTRAINT criteria_applicability_phase1_criterion_fkey
        FOREIGN KEY (criterion_id) REFERENCES public.criteria(id) ON DELETE CASCADE
);

-- Backfill: every existing criterion is applicable to requests.
INSERT INTO public.criteria_applicability_phase1 (criterion_id, applicable_to_requests)
SELECT id, true FROM public.criteria
ON CONFLICT DO NOTHING;
