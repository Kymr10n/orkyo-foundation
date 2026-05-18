-- @migration-class: expand

-- Required for case-insensitive email comparison on person_profiles.email
CREATE EXTENSION IF NOT EXISTS citext;

-- person_profiles: per-resource extras for type='person'
CREATE TABLE person_profiles (
    resource_id   UUID PRIMARY KEY REFERENCES resources(id) ON DELETE CASCADE,
    email         CITEXT NULL,
    job_title     VARCHAR(200) NULL,
    department    VARCHAR(200) NULL,
    linked_user_id UUID NULL UNIQUE REFERENCES users(id) ON DELETE SET NULL,
    notes         TEXT NULL,
    created_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at    TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX CONCURRENTLY ix_person_profiles_linked_user ON person_profiles(linked_user_id) WHERE linked_user_id IS NOT NULL;

-- updated_at auto-maintenance (same pattern as all other tables in this schema)
CREATE TRIGGER trg_person_profiles_updated_at
    BEFORE UPDATE ON public.person_profiles
    FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();

-- group default availability
ALTER TABLE resource_groups
    ADD COLUMN default_availability_percent INT NOT NULL DEFAULT 100
        CHECK (default_availability_percent BETWEEN 0 AND 100);

-- assignment role/notes
ALTER TABLE resource_assignments
    ADD COLUMN role  VARCHAR(100) NULL,
    ADD COLUMN notes TEXT NULL;
