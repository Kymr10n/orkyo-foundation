-- @migration-class: contract

-- Drops `departments`, `job_titles` and the two resource columns that referenced them.
--
-- 1820 copied every row into organization-scoped lists, keeping the primary keys as row ids, and
-- the accompanying code change moved every reader and writer onto the lookup custom fields. These
-- two tables and two columns have been dead weight since.
--
-- Inbound references, all accounted for: `resources.department_id` and `resources.job_title_id`
-- are the only foreign keys into either table, and both are dropped here with their columns.
-- `departments.parent_department_id` is self-referencing and goes with the table. No view reads
-- either one, so there is no view rebuild to sequence around.
--
-- CAUTION: this is the point of no return for the tree. Once the tables are gone, the parent
-- relationships survive only as the descriptive `parent` text column on each list row. Revert of
-- 1830 rebuilds the two tables empty; it cannot rebuild their contents.

BEGIN;

-- ── The search indexer stops reading a table that is going away ───────────────────────────────
--
-- `refresh_search_resource` joined `job_titles` so that a person could be found by their role,
-- and `trg_search_resources_update` names `job_title_id` in its WHEN clause. The trigger's
-- dependency on the column is what blocks the DROP below, so both are rebuilt first.
--
-- The role stays searchable. It is a lookup into an organization list now, so the replacement
-- resolves EVERY organization-scoped lookup on the type — job title, department, and any the
-- tenant adds — through each definition's display column. That is strictly more than the old
-- version found, and it is keyed on scope rather than on a hardcoded field name.

CREATE OR REPLACE FUNCTION refresh_search_resource(p_resource_id UUID) RETURNS VOID AS $$
DECLARE
    v_name         TEXT;
    v_description  TEXT;
    v_type_id      UUID;
    v_type_key     TEXT;
    v_site_id      UUID;
    v_code         TEXT;
    v_email        TEXT;
    v_custom       JSONB;
    v_org_labels   TEXT;
    v_group_name   TEXT;
    v_capabilities TEXT;
    v_keywords     TEXT;
    v_subtitle     TEXT;
BEGIN
    SELECT r.name, r.description, r.resource_type_id, rt.key, r.home_site_id, r.code,
           r.email::text, COALESCE(r.custom_fields, '{}'::jsonb)
      INTO v_name, v_description, v_type_id, v_type_key, v_site_id, v_code, v_email, v_custom
      FROM resources r
      JOIN resource_types rt ON rt.id = r.resource_type_id
     WHERE r.id = p_resource_id;

    -- Gone between the trigger firing and this running, or never existed.
    IF v_name IS NULL THEN
        DELETE FROM search_documents
         WHERE entity_type = 'resource' AND entity_id = p_resource_id;
        RETURN;
    END IF;

    SELECT string_agg(lr.values ->> lc.key, ' ' ORDER BY f.sort_order)
      INTO v_org_labels
      FROM resource_custom_fields f
      JOIN list_instances   li ON li.id = f.list_instance_id
      JOIN list_definitions ld ON ld.id = li.list_definition_id AND ld.scope = 'organization'
      JOIN list_columns     lc ON lc.id = ld.display_column_id
      CROSS JOIN LATERAL jsonb_array_elements_text(
          CASE WHEN jsonb_typeof(v_custom -> f.key) = 'array'
               THEN v_custom -> f.key ELSE '[]'::jsonb END) AS picked(row_id)
      JOIN list_rows lr ON lr.list_instance_id = li.id AND lr.id::text = picked.row_id
     WHERE f.resource_type_id = v_type_id
       AND f.data_type = 'list_lookup'
       AND f.is_active;

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
        nullif(v_org_labels, ''), nullif(v_group_name, ''), nullif(v_description, '')));

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

-- custom_fields replaces job_title_id in the WHEN clause: editing a lookup is now what changes
-- the subtitle, and it is the write the trigger must notice.
DROP TRIGGER IF EXISTS trg_search_resources_update ON public.resources;
CREATE TRIGGER trg_search_resources_update
    AFTER UPDATE ON public.resources
    FOR EACH ROW
    WHEN (OLD.name             IS DISTINCT FROM NEW.name
       OR OLD.description      IS DISTINCT FROM NEW.description
       OR OLD.code             IS DISTINCT FROM NEW.code
       OR OLD.email            IS DISTINCT FROM NEW.email
       OR OLD.home_site_id     IS DISTINCT FROM NEW.home_site_id
       OR OLD.custom_fields    IS DISTINCT FROM NEW.custom_fields
       OR OLD.resource_type_id IS DISTINCT FROM NEW.resource_type_id)
    EXECUTE FUNCTION trg_refresh_search_resource();

ALTER TABLE public.resources
    DROP COLUMN IF EXISTS department_id,
    DROP COLUMN IF EXISTS job_title_id;

DROP TABLE IF EXISTS public.departments;
DROP TABLE IF EXISTS public.job_titles;

COMMIT;
