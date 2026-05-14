-- Revert migration 1310 (Phase 2 cutover).
-- Warning: requests.space_id data is reconstructed from resource_assignments.
-- The revert is lossy if resource_assignments were created after the cutover
-- (Phase-3+ work). Only safe to run immediately after a failed cutover.

-- Restore requests.space_id from resource_assignments
ALTER TABLE requests ADD COLUMN space_id UUID;

UPDATE requests req
SET space_id = ra.resource_id
FROM resource_assignments ra
JOIN resources res ON res.id = ra.resource_id
JOIN resource_types rt ON rt.id = res.resource_type_id
WHERE ra.request_id = req.id
  AND rt.key = 'space'
  AND ra.assignment_status != 'Cancelled';

ALTER TABLE requests
    ADD CONSTRAINT requests_space_id_fkey
        FOREIGN KEY (space_id) REFERENCES spaces (id) ON DELETE SET NULL;

CREATE INDEX idx_requests_space_id ON requests (space_id);
CREATE INDEX idx_requests_scheduling_join
    ON requests (space_id, scheduling_settings_apply, start_ts)
    WHERE scheduling_settings_apply = true AND start_ts IS NOT NULL;

DROP INDEX IF EXISTS idx_ra_scheduling_join;

-- Restore off_times column
ALTER TABLE off_times RENAME COLUMN applies_to_all_resources TO applies_to_all_spaces;

-- Restore off_time_resources → off_time_spaces
ALTER TABLE off_time_resources RENAME COLUMN resource_id TO space_id;
ALTER TABLE off_time_resources
    DROP CONSTRAINT off_time_resources_resource_fkey,
    ADD CONSTRAINT off_time_spaces_space_fkey
        FOREIGN KEY (space_id) REFERENCES spaces (id) ON DELETE CASCADE;
ALTER TABLE off_time_resources RENAME TO off_time_spaces;

-- Restore resource_group_capabilities → group_capabilities
ALTER TABLE resource_group_capabilities RENAME COLUMN resource_group_id TO group_id;
ALTER TABLE resource_group_capabilities RENAME TO group_capabilities;

-- Restore resource_groups → space_groups
ALTER TABLE resource_groups DROP COLUMN resource_type_id;
ALTER TABLE resource_groups RENAME TO space_groups;

-- Restore resource_capabilities → space_capabilities
ALTER TABLE resource_capabilities RENAME COLUMN resource_id TO space_id;
ALTER TABLE resource_capabilities RENAME TO space_capabilities;

-- Remove spaces FK to resources and restore name/desc
ALTER TABLE spaces DROP CONSTRAINT spaces_id_resource_fkey;
-- (name and description cannot be restored without backup)

-- Clear resource_assignments backfill (removes ALL rows — only safe immediately post-cutover)
DELETE FROM resource_assignments;

-- Remove resources rows created from spaces
DELETE FROM resources WHERE id IN (SELECT id FROM spaces);
