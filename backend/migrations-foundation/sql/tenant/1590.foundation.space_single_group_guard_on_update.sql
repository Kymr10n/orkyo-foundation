-- @migration-class: expand

-- Extend the space single-group guard to fire on UPDATE as well as INSERT.
--
-- enforce_space_single_group() (migration 1530) enforces "a space belongs to at
-- most one group", but its trigger only fired BEFORE INSERT. A direct UPDATE of
-- resource_group_members.resource_group_id could therefore move a space into a
-- second group without tripping the guard. The application moves spaces via
-- delete-before-insert (ResourceGroupMemberRepository.SetMembersAsync), so this is
-- defense-in-depth for direct-SQL paths only — no app behavior changes.
--
-- Widening to UPDATE requires excluding the row being moved: on UPDATE the OLD
-- membership row is still present and would otherwise match the "other group"
-- predicate and falsely reject a legitimate G1 -> G2 move. The EXISTS below skips
-- the OLD row on UPDATE; the INSERT path (OLD is NULL) is unchanged.
--
-- Rollback: restore the migration-1530 function body and recreate the trigger as
-- BEFORE INSERT only.

BEGIN;

CREATE OR REPLACE FUNCTION enforce_space_single_group() RETURNS TRIGGER AS $$
DECLARE
    v_space_type uuid;
BEGIN
    SELECT id INTO v_space_type FROM resource_types WHERE key = 'space';

    IF NEW.resource_type_id = v_space_type THEN
        IF EXISTS (
            SELECT 1
            FROM resource_group_members m
            JOIN resource_groups g ON g.id = m.resource_group_id
            WHERE m.resource_id = NEW.resource_id
              AND m.resource_group_id <> NEW.resource_group_id
              AND g.resource_type_id = v_space_type
              -- On UPDATE, ignore the row currently being moved.
              AND (TG_OP <> 'UPDATE'
                   OR m.resource_group_id <> OLD.resource_group_id
                   OR m.resource_id <> OLD.resource_id)
        ) THEN
            RAISE EXCEPTION 'space % is already a member of another group', NEW.resource_id
                USING ERRCODE = 'unique_violation';
        END IF;
    END IF;

    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_space_single_group ON resource_group_members;
CREATE TRIGGER trg_space_single_group
    BEFORE INSERT OR UPDATE ON resource_group_members
    FOR EACH ROW EXECUTE FUNCTION enforce_space_single_group();

COMMIT;
