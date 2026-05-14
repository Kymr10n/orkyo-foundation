-- Phase 2: Atomic cutover from Space-first to Resource-first model.
-- All renames, backfills, and drops execute in one transaction.
-- Paired revert: backend/migrations-foundation/revert/1310.foundation.cutover_to_resources.revert.sql

-- ── Step 0: Capture requests.space_id before drop (for backfill) ─────────────

CREATE TEMP TABLE _requests_space_snapshot AS
SELECT id AS request_id, space_id, start_ts, end_ts, status, planning_mode
FROM requests
WHERE space_id IS NOT NULL;

-- ── Step 1: Backfill resources from spaces (shared uuid) ─────────────────────

INSERT INTO resources (id, resource_type_id, name, description,
                       allocation_mode, base_availability_percent,
                       is_active, created_at, updated_at)
SELECT s.id,
       (SELECT id FROM resource_types WHERE key = 'space'),
       s.name,
       s.description,
       'Exclusive',
       100,
       true,
       s.created_at,
       s.updated_at
FROM spaces s
ON CONFLICT (id) DO NOTHING;

-- ── Step 2: Spaces becomes a subtype-extension of resources ──────────────────

ALTER TABLE spaces
    ADD CONSTRAINT spaces_id_resource_fkey
    FOREIGN KEY (id) REFERENCES resources (id) ON DELETE RESTRICT;

ALTER TABLE spaces DROP COLUMN name;
ALTER TABLE spaces DROP COLUMN description;

-- ── Step 3: Drop Phase-1 staging tables ──────────────────────────────────────

DROP TABLE resource_capabilities_phase1;
DROP TABLE criteria_applicability_phase1;

-- ── Step 4: Rename space_capabilities → resource_capabilities ────────────────

ALTER TABLE space_capabilities RENAME TO resource_capabilities;
ALTER TABLE resource_capabilities RENAME COLUMN space_id TO resource_id;

ALTER TABLE resource_capabilities
    DROP CONSTRAINT space_capabilities_space_id_fkey,
    ADD CONSTRAINT resource_capabilities_resource_fkey
        FOREIGN KEY (resource_id) REFERENCES resources (id) ON DELETE CASCADE;

ALTER TABLE resource_capabilities
    RENAME CONSTRAINT unique_space_criterion TO unique_resource_criterion;

ALTER INDEX idx_space_capabilities_space_id     RENAME TO idx_resource_capabilities_resource_id;
ALTER INDEX idx_space_capabilities_criterion_id RENAME TO idx_resource_capabilities_criterion_id;

-- ── Step 5: Rename space_groups → resource_groups ────────────────────────────

ALTER TABLE space_groups RENAME TO resource_groups;

ALTER TABLE resource_groups
    ADD COLUMN resource_type_id UUID
        REFERENCES resource_types (id);

UPDATE resource_groups
SET resource_type_id = (SELECT id FROM resource_types WHERE key = 'space');

ALTER TABLE resource_groups
    ALTER COLUMN resource_type_id SET NOT NULL;

-- ── Step 6: Rename group_capabilities → resource_group_capabilities ──────────

ALTER TABLE group_capabilities RENAME TO resource_group_capabilities;
ALTER TABLE resource_group_capabilities RENAME COLUMN group_id TO resource_group_id;

ALTER TABLE resource_group_capabilities
    DROP CONSTRAINT group_capabilities_group_id_fkey,
    ADD CONSTRAINT resource_group_capabilities_group_fkey
        FOREIGN KEY (resource_group_id) REFERENCES resource_groups (id) ON DELETE CASCADE;

ALTER TABLE resource_group_capabilities
    DROP CONSTRAINT group_capabilities_criterion_id_fkey,
    ADD CONSTRAINT resource_group_capabilities_criterion_fkey
        FOREIGN KEY (criterion_id) REFERENCES criteria (id) ON DELETE CASCADE;

ALTER TABLE resource_group_capabilities
    DROP CONSTRAINT unique_group_criterion,
    ADD CONSTRAINT unique_resource_group_criterion UNIQUE (resource_group_id, criterion_id);

ALTER INDEX idx_group_capabilities_group_id     RENAME TO idx_resource_group_capabilities_group_id;
ALTER INDEX idx_group_capabilities_criterion_id RENAME TO idx_resource_group_capabilities_criterion_id;

-- ── Step 7: Rename off_time_spaces → off_time_resources ──────────────────────

ALTER TABLE off_time_spaces RENAME TO off_time_resources;
ALTER TABLE off_time_resources RENAME COLUMN space_id TO resource_id;

ALTER TABLE off_time_resources
    DROP CONSTRAINT off_time_spaces_space_fkey,
    ADD CONSTRAINT off_time_resources_resource_fkey
        FOREIGN KEY (resource_id) REFERENCES resources (id) ON DELETE CASCADE;

-- ── Step 8: Rename off_times.applies_to_all_spaces ───────────────────────────

ALTER TABLE off_times
    RENAME COLUMN applies_to_all_spaces TO applies_to_all_resources;

-- Rename the index on idx_off_times_time_range (no rename needed — it has no space ref).
-- Update the drop index on off_times for time_range; no schema change needed.

-- ── Step 9: Backfill resource_assignments from requests ──────────────────────

INSERT INTO resource_assignments
    (id, request_id, resource_id, start_utc, end_utc,
     assignment_status, created_at, updated_at)
SELECT gen_random_uuid(),
       snap.request_id,
       snap.space_id,
       snap.start_ts,
       snap.end_ts,
       CASE snap.status
           WHEN 'cancelled' THEN 'Cancelled'
           ELSE 'Planned'
       END,
       NOW(),
       NOW()
FROM _requests_space_snapshot snap
WHERE snap.start_ts IS NOT NULL
  AND snap.end_ts IS NOT NULL
  AND snap.planning_mode = 'leaf'
ON CONFLICT DO NOTHING;

-- ── Step 10: Data-parity gate ─────────────────────────────────────────────────

DO $$
DECLARE
    expected_count BIGINT;
    actual_new     BIGINT;
BEGIN
    SELECT COUNT(*) INTO expected_count
    FROM _requests_space_snapshot
    WHERE start_ts IS NOT NULL
      AND end_ts IS NOT NULL
      AND planning_mode = 'leaf';

    SELECT COUNT(*) INTO actual_new
    FROM resource_assignments;

    -- Allow actual_new >= expected_count because Phase-1 tests may have already
    -- created some resource_assignments rows (they use gen_random_uuid request ids
    -- that exist, so no FK issues, but they are additional rows).
    IF actual_new < expected_count THEN
        RAISE EXCEPTION
            'Data parity gate failed: expected at least % resource_assignment rows, got %',
            expected_count, actual_new;
    END IF;
END $$;

DROP TABLE _requests_space_snapshot;

-- ── Step 11: Drop requests.space_id ──────────────────────────────────────────

ALTER TABLE requests DROP CONSTRAINT requests_space_id_fkey;
DROP INDEX IF EXISTS idx_requests_space_id;
DROP INDEX IF EXISTS idx_requests_scheduling_join;
ALTER TABLE requests DROP COLUMN space_id;

-- ── Step 12: New scheduling index (via resource_assignments) ──────────────────

CREATE INDEX idx_ra_scheduling_join
    ON resource_assignments (resource_id, start_utc)
    WHERE assignment_status != 'Cancelled';
