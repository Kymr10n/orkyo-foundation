-- @migration-class: expand
--
-- Phase 1 of resource-group type enforcement (expand → data → contract).
-- Adds the unique composite keys on resources and resource_groups that the
-- later composite FKs on resource_group_members will reference, and adds the
-- resource_type_id column (nullable for now) to resource_group_members.
-- Purely additive — safe to roll back.

-- Composite unique constraint so (id, resource_type_id) can be a FK target.
ALTER TABLE resources
    ADD CONSTRAINT resources_id_type_uk UNIQUE (id, resource_type_id);

ALTER TABLE resource_groups
    ADD CONSTRAINT resource_groups_id_type_uk UNIQUE (id, resource_type_id);

-- Add the denormalised resource_type_id column; nullable until migration 1460.
ALTER TABLE resource_group_members
    ADD COLUMN resource_type_id UUID;
