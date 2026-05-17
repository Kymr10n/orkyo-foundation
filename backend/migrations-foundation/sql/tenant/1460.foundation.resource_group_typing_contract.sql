-- @migration-class: contract
--
-- Phase 3 of resource-group type enforcement. NOT rollback-safe.
-- Requires APPROVE_UNSAFE_MIGRATION=1.
--
-- Narrows resource_group_members.resource_type_id to NOT NULL and adds
-- composite FK constraints that make cross-type membership impossible at the
-- database level. Also removes the temporary DEFAULT shim introduced in
-- migration 1330 and drops the helper function it depended on.

-- Set NOT NULL now that the backfill (1450) guarantees every row has a value.
ALTER TABLE resource_group_members
    ALTER COLUMN resource_type_id SET NOT NULL;

-- Composite FK: a member row's (resource_group_id, resource_type_id) must point
-- to a real group whose type matches. ON DELETE CASCADE keeps membership clean
-- when a group is deleted.
ALTER TABLE resource_group_members
    ADD CONSTRAINT rgm_group_type_fk
    FOREIGN KEY (resource_group_id, resource_type_id)
    REFERENCES resource_groups (id, resource_type_id)
    ON DELETE CASCADE;

-- Composite FK: a member row's (resource_id, resource_type_id) must point to a
-- real resource whose type matches. ON DELETE CASCADE removes stale member rows
-- when a resource is deleted.
ALTER TABLE resource_group_members
    ADD CONSTRAINT rgm_resource_type_fk
    FOREIGN KEY (resource_id, resource_type_id)
    REFERENCES resources (id, resource_type_id)
    ON DELETE CASCADE;

-- Remove the DEFAULT shim that allowed SpaceGroupRepository to insert into
-- resource_groups without supplying resource_type_id. The SpaceGroup* write
-- path is deleted in the same PR, so this default is no longer needed.
ALTER TABLE resource_groups
    ALTER COLUMN resource_type_id DROP DEFAULT;

-- Drop the helper function the DEFAULT shim depended on.
DROP FUNCTION IF EXISTS get_space_resource_type_id();
