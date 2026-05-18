ALTER TABLE resource_assignments
    DROP COLUMN IF EXISTS notes,
    DROP COLUMN IF EXISTS role;

ALTER TABLE resource_groups
    DROP COLUMN IF EXISTS default_availability_percent;

DROP TABLE IF EXISTS person_profiles;
