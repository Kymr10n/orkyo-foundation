-- @migration-class: expand

-- ── Index person resources in search_documents ───────────────────────────────
--
-- People were never added to the search index when the global search was
-- built (migration 1280). This migration adds the sync trigger and backfills
-- existing person resources so they appear in the command palette.
--
-- Trigger fires on resources (filtered to person type) and on person_profiles
-- so that email / job-title changes also update the index.

-- ── Trigger function ─────────────────────────────────────────────────────────

CREATE OR REPLACE FUNCTION sync_search_people() RETURNS TRIGGER AS $$
DECLARE
    v_resource_id UUID;
    v_name        TEXT;
    v_email       TEXT;
    v_job_title   TEXT;
    v_person_type_id UUID;
BEGIN
    SELECT id INTO v_person_type_id FROM resource_types WHERE key = 'person' LIMIT 1;

    -- Determine the resource_id depending on which table fired the trigger.
    IF TG_TABLE_NAME = 'resources' THEN
        -- Only index person-type resources.
        IF TG_OP = 'DELETE' THEN
            IF OLD.resource_type_id = v_person_type_id THEN
                DELETE FROM search_documents WHERE entity_type = 'person' AND entity_id = OLD.id;
            END IF;
            RETURN OLD;
        END IF;
        IF NEW.resource_type_id != v_person_type_id THEN
            RETURN NEW;
        END IF;
        v_resource_id := NEW.id;
    ELSE
        -- person_profiles table — resolve resource_id from the row.
        IF TG_OP = 'DELETE' THEN
            DELETE FROM search_documents WHERE entity_type = 'person' AND entity_id = OLD.resource_id;
            RETURN OLD;
        END IF;
        v_resource_id := NEW.resource_id;
    END IF;

    SELECT r.name INTO v_name FROM resources r WHERE r.id = v_resource_id;

    SELECT pp.email, jt.name
    INTO v_email, v_job_title
    FROM person_profiles pp
    LEFT JOIN job_titles jt ON jt.id = pp.job_title_id
    WHERE pp.resource_id = v_resource_id;

    INSERT INTO search_documents (entity_type, entity_id, site_id, title, subtitle, keywords, fts, updated_at)
    VALUES (
        'person', v_resource_id, NULL,
        v_name,
        coalesce(v_job_title, ''),
        coalesce(v_email, ''),
        build_search_fts(
            v_name,
            coalesce(v_email, ''),
            coalesce(v_job_title, '')
        ),
        now()
    )
    ON CONFLICT (entity_type, entity_id) DO UPDATE SET
        title      = EXCLUDED.title,
        subtitle   = EXCLUDED.subtitle,
        keywords   = EXCLUDED.keywords,
        fts        = EXCLUDED.fts,
        updated_at = EXCLUDED.updated_at;

    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- ── Triggers ─────────────────────────────────────────────────────────────────

CREATE TRIGGER sync_search_resources_people
    AFTER INSERT OR UPDATE OR DELETE ON resources
    FOR EACH ROW EXECUTE FUNCTION sync_search_people();

CREATE TRIGGER sync_search_person_profiles
    AFTER INSERT OR UPDATE OR DELETE ON person_profiles
    FOR EACH ROW EXECUTE FUNCTION sync_search_people();

-- ── Backfill ─────────────────────────────────────────────────────────────────

INSERT INTO search_documents (entity_type, entity_id, site_id, title, subtitle, keywords, fts, updated_at)
SELECT
    'person',
    r.id,
    NULL,
    r.name,
    coalesce(jt.name, ''),
    coalesce(pp.email, ''),
    build_search_fts(
        r.name,
        coalesce(pp.email, ''),
        coalesce(jt.name, '')
    ),
    now()
FROM resources r
JOIN resource_types rt ON rt.id = r.resource_type_id AND rt.key = 'person'
LEFT JOIN person_profiles pp ON pp.resource_id = r.id
LEFT JOIN job_titles jt ON jt.id = pp.job_title_id
ON CONFLICT (entity_type, entity_id) DO UPDATE SET
    title      = EXCLUDED.title,
    subtitle   = EXCLUDED.subtitle,
    keywords   = EXCLUDED.keywords,
    fts        = EXCLUDED.fts,
    updated_at = EXCLUDED.updated_at;
