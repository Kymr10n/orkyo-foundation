-- Reverts 1830. The two tables and the two resource columns come back, EMPTY.
--
-- This restores structure, not data. 1830 dropped the rows, and the surviving copy lives in
-- organization list rows whose `parent` is a name rather than an id, so a faithful rebuild of the
-- tree is not something a script can do — a duplicated department name has no single correct
-- parent to attach to.
--
-- The intended recovery from a bad 1830 is a restore from backup, not this script. It exists so
-- that a database can be walked back to the 1820 schema, at which point 1820's own revert can run
-- and put the (still empty) tables back in charge.
--
-- The unique indexes are recreated without CONCURRENTLY: the original used it against a live
-- table, and a revert runs inside this transaction, where CONCURRENTLY is not allowed.

BEGIN;

CREATE TABLE IF NOT EXISTS public.job_titles (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name        VARCHAR(200) NOT NULL UNIQUE,
    description TEXT NULL,
    is_active   BOOLEAN NOT NULL DEFAULT TRUE,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_job_titles_active
    ON public.job_titles (is_active) WHERE is_active;

CREATE TABLE IF NOT EXISTS public.departments (
    id                   UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    parent_department_id UUID NULL REFERENCES public.departments(id) ON DELETE RESTRICT,
    name                 VARCHAR(200) NOT NULL,
    code                 VARCHAR(50) NULL,
    description          TEXT NULL,
    is_active            BOOLEAN NOT NULL DEFAULT TRUE,
    created_at           TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at           TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT departments_no_self_parent CHECK (id <> parent_department_id)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_departments_root_name
    ON public.departments (name) WHERE parent_department_id IS NULL;

CREATE UNIQUE INDEX IF NOT EXISTS ux_departments_sibling_name
    ON public.departments (parent_department_id, name) WHERE parent_department_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_departments_parent
    ON public.departments (parent_department_id);

CREATE INDEX IF NOT EXISTS ix_departments_active
    ON public.departments (is_active) WHERE is_active;

CREATE UNIQUE INDEX IF NOT EXISTS ux_departments_code
    ON public.departments (code) WHERE code IS NOT NULL;

ALTER TABLE public.resources
    ADD COLUMN IF NOT EXISTS job_title_id  UUID NULL REFERENCES public.job_titles(id)  ON DELETE SET NULL,
    ADD COLUMN IF NOT EXISTS department_id UUID NULL REFERENCES public.departments(id) ON DELETE SET NULL;

-- ── Search indexing goes back to reading job_titles ───────────────────────────────────────────
--
-- 1830 rewrote `refresh_search_resource` to resolve the role through organization list lookups and
-- dropped `job_title_id` from the trigger's WHEN clause. With the column restored above, leaving
-- the new definitions in place would mean a job-title change never refreshes the search document —
-- a silent gap, so both go back to their 1710 form.

CREATE OR REPLACE FUNCTION refresh_search_resource(p_resource_id UUID) RETURNS VOID AS $$
DECLARE
    v_name         TEXT;
    v_description  TEXT;
    v_type_key     TEXT;
    v_site_id      UUID;
    v_code         TEXT;
    v_email        TEXT;
    v_job_title    TEXT;
    v_group_name   TEXT;
    v_capabilities TEXT;
    v_keywords     TEXT;
    v_subtitle     TEXT;
BEGIN
    SELECT r.name, r.description, rt.key, r.home_site_id, r.code, r.email::text, jt.name
      INTO v_name, v_description, v_type_key, v_site_id, v_code, v_email, v_job_title
      FROM resources r
      JOIN resource_types rt ON rt.id = r.resource_type_id
      LEFT JOIN job_titles jt ON jt.id = r.job_title_id
     WHERE r.id = p_resource_id;

    -- Gone between the trigger firing and this running, or never existed.
    IF v_name IS NULL THEN
        DELETE FROM search_documents
         WHERE entity_type = 'resource' AND entity_id = p_resource_id;
        RETURN;
    END IF;

    SELECT g.name INTO v_group_name
      FROM resource_group_members m
      JOIN resource_groups g ON g.id = m.resource_group_id
     WHERE m.resource_id = p_resource_id
     LIMIT 1;

    SELECT string_agg(c.name, ' ') INTO v_capabilities
      FROM resource_capabilities rc
      JOIN criteria c ON c.id = rc.criterion_id
     WHERE rc.resource_id = p_resource_id;

    -- The type key is a keyword so "tool" or "vehicle" finds the whole class.
    v_keywords := trim(concat_ws(' ',
        v_type_key, v_code, v_email, nullif(v_capabilities, '')));
    v_subtitle := trim(concat_ws(' ',
        nullif(v_job_title, ''), nullif(v_group_name, ''), nullif(v_description, '')));

    INSERT INTO search_documents (
        entity_type, entity_id, site_id, title, subtitle, keywords, fts,
        resource_type_key, updated_at)
    VALUES (
        'resource', p_resource_id, v_site_id, v_name,
        nullif(v_subtitle, ''), nullif(v_keywords, ''),
        build_search_fts(v_name, v_keywords, v_subtitle),
        v_type_key, now())
    ON CONFLICT (entity_type, entity_id) DO UPDATE SET
        site_id           = EXCLUDED.site_id,
        title             = EXCLUDED.title,
        subtitle          = EXCLUDED.subtitle,
        keywords          = EXCLUDED.keywords,
        fts               = EXCLUDED.fts,
        resource_type_key = EXCLUDED.resource_type_key,
        updated_at        = EXCLUDED.updated_at;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_search_resources_update ON public.resources;
CREATE TRIGGER trg_search_resources_update
    AFTER UPDATE ON public.resources
    FOR EACH ROW
    WHEN (OLD.name             IS DISTINCT FROM NEW.name
       OR OLD.description      IS DISTINCT FROM NEW.description
       OR OLD.code             IS DISTINCT FROM NEW.code
       OR OLD.email            IS DISTINCT FROM NEW.email
       OR OLD.home_site_id     IS DISTINCT FROM NEW.home_site_id
       OR OLD.job_title_id     IS DISTINCT FROM NEW.job_title_id
       OR OLD.resource_type_id IS DISTINCT FROM NEW.resource_type_id)
    EXECUTE FUNCTION trg_refresh_search_resource();

COMMIT;
