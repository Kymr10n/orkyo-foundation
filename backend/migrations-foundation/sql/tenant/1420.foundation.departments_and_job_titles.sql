-- @migration-class: contract
--
-- Adds two reference tables (job_titles, departments) and rewires person_profiles
-- to reference them by FK instead of carrying free-text values. Departments are
-- hierarchical (self-referencing); job_titles are flat.
--
-- Single-file migration with the column drops on person_profiles is acceptable
-- here because the People Resources spec is a clean break — no production
-- tenants have free-text values that need backfilling. If that assumption
-- changes later, split into expand + contract migrations and add a backfill
-- step between them.

-- ── job_titles ──────────────────────────────────────────────────────────────
CREATE TABLE public.job_titles (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name        VARCHAR(200) NOT NULL UNIQUE,
    description TEXT NULL,
    is_active   BOOLEAN NOT NULL DEFAULT TRUE,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX ix_job_titles_active ON public.job_titles (is_active) WHERE is_active;

-- ── departments ─────────────────────────────────────────────────────────────
-- Tenant-wide tree: a single hierarchy per tenant database. parent_department_id
-- IS NULL identifies a root. ON DELETE RESTRICT prevents accidentally removing
-- a department with children.
CREATE TABLE public.departments (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    parent_department_id    UUID NULL REFERENCES public.departments(id) ON DELETE RESTRICT,
    name                    VARCHAR(200) NOT NULL,
    code                    VARCHAR(50) NULL,
    description             TEXT NULL,
    is_active               BOOLEAN NOT NULL DEFAULT TRUE,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT departments_no_self_parent CHECK (id <> parent_department_id)
);

-- Sibling-name uniqueness. Postgres treats NULL as distinct in regular UNIQUE
-- constraints, so we split into two partial indexes — one for roots (NULL
-- parent), one for non-roots — to enforce uniqueness in both shapes.
CREATE UNIQUE INDEX ux_departments_root_name
    ON public.departments (name)
    WHERE parent_department_id IS NULL;
CREATE UNIQUE INDEX ux_departments_sibling_name
    ON public.departments (parent_department_id, name)
    WHERE parent_department_id IS NOT NULL;

CREATE INDEX ix_departments_parent ON public.departments (parent_department_id);
CREATE INDEX ix_departments_active ON public.departments (is_active) WHERE is_active;
-- Optional department codes (e.g. cost-centre codes) must be unique when present.
CREATE UNIQUE INDEX ux_departments_code ON public.departments (code) WHERE code IS NOT NULL;

-- updated_at triggers reuse the function defined in migration 1100
CREATE TRIGGER trg_job_titles_updated_at
    BEFORE UPDATE ON public.job_titles
    FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();
CREATE TRIGGER trg_departments_updated_at
    BEFORE UPDATE ON public.departments
    FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();

-- ── person_profiles rewiring ────────────────────────────────────────────────
-- Drop the legacy free-text columns and replace with FK references. ON DELETE
-- SET NULL means deleting a job_title or department leaves the person row
-- intact; deactivation is the recommended way to retire a reference value.
ALTER TABLE public.person_profiles
    DROP COLUMN job_title,
    DROP COLUMN department,
    ADD COLUMN job_title_id  UUID NULL REFERENCES public.job_titles(id)  ON DELETE SET NULL,
    ADD COLUMN department_id UUID NULL REFERENCES public.departments(id) ON DELETE SET NULL;

CREATE INDEX ix_person_profiles_job_title_id
    ON public.person_profiles (job_title_id)
    WHERE job_title_id IS NOT NULL;
CREATE INDEX ix_person_profiles_department_id
    ON public.person_profiles (department_id)
    WHERE department_id IS NOT NULL;
