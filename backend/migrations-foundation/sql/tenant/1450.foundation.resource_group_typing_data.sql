-- @migration-class: data
--
-- Phase 2 of resource-group type enforcement.
-- Backfills resource_type_id on resource_group_members from the joined resource
-- row, then fails loudly if any member's type does not match its group's type.
-- Old schema still satisfies application code — safe to roll back to 1440 state.

-- Backfill: copy resource_type_id from the resource row.
UPDATE resource_group_members m
SET resource_type_id = r.resource_type_id
FROM resources r
WHERE r.id = m.resource_id;

-- Guard: abort if any existing member has a type that differs from its group.
-- A non-zero count means data was inserted through a path that bypassed type
-- checking. Resolve manually before rerunning.
DO $$
DECLARE mismatch_count INT;
BEGIN
    SELECT COUNT(*) INTO mismatch_count
    FROM resource_group_members m
    JOIN resource_groups g ON g.id = m.resource_group_id
    WHERE m.resource_type_id IS DISTINCT FROM g.resource_type_id;

    IF mismatch_count > 0 THEN
        RAISE EXCEPTION
            'resource_group_members type mismatch: % row(s) have a resource_type_id '
            'that does not match their group. Resolve before running this migration.',
            mismatch_count;
    END IF;
END $$;
