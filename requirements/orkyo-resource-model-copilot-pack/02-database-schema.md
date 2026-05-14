# Database Schema — Final Target

All tenant tables. PostgreSQL 16. `public` schema. Migration numbers
continue from existing tenant migrations (last is `1290.foundation.*`).

This document describes the **final** post-cutover schema. Phase 1
introduces some of these objects alongside legacy tables; Phase 2
renames the legacy tables into place and drops `requests.space_id`.
See `04-phase-1-parallel-build.md` and `05-phase-2-cutover.md` for the
exact migration SQL of each phase.

## Tables added or modified

### `resource_types` (NEW)
```sql
CREATE TABLE resource_types (
  id           UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  key          VARCHAR(50) NOT NULL UNIQUE,
  display_name VARCHAR(100) NOT NULL,
  description  TEXT,
  is_system    BOOLEAN NOT NULL DEFAULT false,
  is_active    BOOLEAN NOT NULL DEFAULT true,
  created_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
  updated_at   TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE TRIGGER resource_types_updated_at
  BEFORE UPDATE ON resource_types
  FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();

INSERT INTO resource_types (key, display_name, is_system, is_active) VALUES
  ('space',  'Space',  true, true),
  ('person', 'Person', true, true),
  ('tool',   'Tool',   true, true);
```

### `resources` (NEW)
```sql
CREATE TABLE resources (
  id                        UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  resource_type_id          UUID NOT NULL REFERENCES resource_types(id),
  name                      VARCHAR(255) NOT NULL,
  description               TEXT,
  external_reference        VARCHAR(255),
  allocation_mode           VARCHAR(30) NOT NULL
    CHECK (allocation_mode IN ('Exclusive','Fractional','ConcurrentCapacity')),
  base_availability_percent INT NOT NULL DEFAULT 100
    CHECK (base_availability_percent BETWEEN 0 AND 100),
  is_active                 BOOLEAN NOT NULL DEFAULT true,
  metadata_json             JSONB,
  created_at                TIMESTAMPTZ NOT NULL DEFAULT now(),
  updated_at                TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX idx_resources_type    ON resources(resource_type_id);
CREATE INDEX idx_resources_active  ON resources(is_active) WHERE is_active;
CREATE TRIGGER resources_updated_at
  BEFORE UPDATE ON resources
  FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();
```

### `spaces` (MODIFIED — subtype of `resources`)
After Phase 2:
```sql
-- spaces.id is a FK to resources.id (same uuid).
ALTER TABLE spaces
  ADD CONSTRAINT spaces_id_resource_fkey
  FOREIGN KEY (id) REFERENCES resources(id) ON DELETE RESTRICT;

-- Name and description live on resources now.
ALTER TABLE spaces DROP COLUMN name;
ALTER TABLE spaces DROP COLUMN description;
```

Remaining columns: `id`, `site_id`, `code`, `is_physical`, `geometry`,
`properties`, `group_id` (now FK to `resource_groups`), `capacity`,
`created_at`, `updated_at`.

### `resource_groups` (RENAMED from `space_groups`)
```sql
ALTER TABLE space_groups RENAME TO resource_groups;
ALTER TABLE resource_groups
  ADD COLUMN resource_type_id UUID
    REFERENCES resource_types(id);
UPDATE resource_groups
  SET resource_type_id = (SELECT id FROM resource_types WHERE key='space');
ALTER TABLE resource_groups
  ALTER COLUMN resource_type_id SET NOT NULL;
```

`spaces.group_id` continues to reference `resource_groups(id)`.

### `resource_group_capabilities` (RENAMED from `group_capabilities`)
```sql
ALTER TABLE group_capabilities RENAME TO resource_group_capabilities;
ALTER TABLE resource_group_capabilities
  RENAME COLUMN group_id TO resource_group_id;
-- FK rename
ALTER TABLE resource_group_capabilities
  RENAME CONSTRAINT group_capabilities_group_id_fkey
                 TO resource_group_capabilities_group_fkey;
```

### `resource_capabilities` (RENAMED from `space_capabilities`)
```sql
ALTER TABLE space_capabilities RENAME TO resource_capabilities;
ALTER TABLE resource_capabilities RENAME COLUMN space_id TO resource_id;
ALTER TABLE resource_capabilities
  DROP CONSTRAINT space_capabilities_space_id_fkey,
  ADD CONSTRAINT resource_capabilities_resource_fkey
    FOREIGN KEY (resource_id) REFERENCES resources(id) ON DELETE CASCADE;
ALTER TABLE resource_capabilities
  RENAME CONSTRAINT unique_space_criterion TO unique_resource_criterion;
ALTER INDEX idx_space_capabilities_space_id     RENAME TO idx_resource_capabilities_resource_id;
ALTER INDEX idx_space_capabilities_criterion_id RENAME TO idx_resource_capabilities_criterion_id;
```

### `criteria` (MODIFIED — applicability tagging)
Added in Phase 3:
```sql
ALTER TABLE criteria
  ADD COLUMN applicable_to_requests BOOLEAN NOT NULL DEFAULT true;

CREATE TABLE criterion_resource_types (
  criterion_id     UUID NOT NULL REFERENCES criteria(id)        ON DELETE CASCADE,
  resource_type_id UUID NOT NULL REFERENCES resource_types(id)  ON DELETE CASCADE,
  PRIMARY KEY (criterion_id, resource_type_id)
);

INSERT INTO criterion_resource_types (criterion_id, resource_type_id)
SELECT c.id, (SELECT id FROM resource_types WHERE key='space')
FROM   criteria c;
```

`request_requirements` and `request_template_requirements` are
unchanged; they continue to reference `criteria(id)`.

### `resource_assignments` (NEW)
```sql
CREATE TABLE resource_assignments (
  id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  request_id          UUID NOT NULL REFERENCES requests(id)  ON DELETE CASCADE,
  resource_id         UUID NOT NULL REFERENCES resources(id) ON DELETE RESTRICT,
  start_utc           TIMESTAMPTZ NOT NULL,
  end_utc             TIMESTAMPTZ NOT NULL,
  allocation_percent  NUMERIC(5,2),  -- NULL for Exclusive; 0<x<=100 for Fractional
  allocation_units    INT,           -- reserved for ConcurrentCapacity
  assignment_status   VARCHAR(20) NOT NULL DEFAULT 'Planned'
    CHECK (assignment_status IN ('Planned','Confirmed','Tentative','Cancelled')),
  created_at          TIMESTAMPTZ NOT NULL DEFAULT now(),
  updated_at          TIMESTAMPTZ NOT NULL DEFAULT now(),
  CONSTRAINT resource_assignments_time_range CHECK (end_utc > start_utc),
  CONSTRAINT resource_assignments_alloc_pct_range
    CHECK (allocation_percent IS NULL OR (allocation_percent > 0 AND allocation_percent <= 100))
);

CREATE INDEX idx_ra_conflict
  ON resource_assignments(resource_id, start_utc, end_utc)
  WHERE assignment_status != 'Cancelled';

CREATE INDEX idx_ra_request
  ON resource_assignments(request_id)
  WHERE assignment_status != 'Cancelled';

CREATE UNIQUE INDEX ux_ra_active_request_resource
  ON resource_assignments(request_id, resource_id)
  WHERE assignment_status != 'Cancelled';

CREATE TRIGGER resource_assignments_updated_at
  BEFORE UPDATE ON resource_assignments
  FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();
```

### `requests` (MODIFIED — drop space_id)
After Phase 2:
```sql
ALTER TABLE requests DROP CONSTRAINT requests_space_id_fkey;
DROP INDEX  idx_requests_space_id;
DROP INDEX  idx_requests_scheduling_join;
ALTER TABLE requests DROP COLUMN space_id;
-- New scheduling index keyed on resource_assignments instead:
CREATE INDEX idx_ra_scheduling_join
  ON resource_assignments(resource_id, start_utc)
  WHERE assignment_status != 'Cancelled';
```

### `off_time_resources` (RENAMED from `off_time_spaces`)
```sql
ALTER TABLE off_time_spaces RENAME TO off_time_resources;
ALTER TABLE off_time_resources RENAME COLUMN space_id TO resource_id;
ALTER TABLE off_time_resources
  DROP CONSTRAINT off_time_spaces_space_fkey,
  ADD CONSTRAINT off_time_resources_resource_fkey
    FOREIGN KEY (resource_id) REFERENCES resources(id) ON DELETE CASCADE;
```

### `off_times` (MODIFIED)
```sql
ALTER TABLE off_times
  RENAME COLUMN applies_to_all_spaces TO applies_to_all_resources;
```

## What is removed

- `requests.space_id` column + its FK and indexes.
- `space_capabilities` (renamed; data preserved).
- `group_capabilities` (renamed; data preserved).
- `space_groups` (renamed; data preserved).
- `off_time_spaces` (renamed; data preserved).
- `spaces.name`, `spaces.description` (moved to `resources`).
- All occurrences of `applies_to_all_spaces` (renamed).

## Migration ordering and revert scripts

Each phase migration in `backend/migrations-foundation/sql/tenant/`
ships with a paired revert SQL file under a `revert/` subdirectory.
See phase-specific docs for exact migration body and revert script.

## Schema diagram (post-cutover)

```
                  ┌─────────────────┐
                  │ resource_types  │
                  └────────┬────────┘
                           │
                  ┌────────▼────────┐
       ┌──────────┤    resources    ├────────────┐
       │          └────────┬────────┘            │
       │                   │                     │
┌──────▼──────┐  ┌─────────▼─────────┐  ┌───────▼────────┐
│   spaces    │  │ resource_         │  │ resource_      │
│ (subtype)   │  │   capabilities    │  │   assignments  │
└──────┬──────┘  └──────────┬────────┘  └───────┬────────┘
       │                    │                   │
       │             ┌──────▼──────┐    ┌───────▼─────────┐
       │             │   criteria  │    │     requests    │
       │             └─────────────┘    └─────────────────┘
       │
┌──────▼─────────────┐
│  resource_groups   │
└──────┬─────────────┘
       │
┌──────▼───────────────────────────────┐
│  resource_group_capabilities         │
└──────────────────────────────────────┘

┌──────────────┐      ┌────────────────────────┐
│   off_times  │──────┤   off_time_resources   │
└──────────────┘      └────────────────────────┘
                            │
                            ▼
                         resources
```
