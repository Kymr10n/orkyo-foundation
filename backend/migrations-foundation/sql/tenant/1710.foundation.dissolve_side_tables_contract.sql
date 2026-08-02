-- @migration-class: contract

-- Drops the `spaces` and `person_profiles` side tables. 1700 copied every column onto
-- `resources` and the accompanying code change moved every reader and writer across, so
-- these two tables have been dead weight since. This is the point at which adding a resource
-- type stops requiring a schema change.
--
-- Safe to drop outright: neither table has a single inbound foreign key. The three that once
-- existed — space_capabilities.space_id, requests.space_id, off_time_spaces.space_id — were
-- all re-pointed at resources(id) by migration 1310 or removed with their tables. No view
-- references either one either, so there is no view rebuild to sequence around.
--
-- Also retires the last two hard-coded type-key tests in SQL: the single-group guard looked
-- up resource_types.key = 'space' on every group membership write, and the search indexer
-- probed both side tables for every resource regardless of type. Both now read the flags 1700
-- added, so a tenant-defined type can opt into either behaviour.
--
-- Rollback: not automatic, and not lossless once this has run — the columns it drops are the
-- only remaining copy. Recreate both tables from 1160/1400/1420, repopulate them from
-- resources, then revert 1700. Restore this file's three replaced functions from 1590 and
-- 1690 as they stood.

BEGIN;

-- ── Single-group guard reads the flag ─────────────────────────────────────────
-- Same rule, no longer spelled 'space'. The lookup by key also went to resource_types on
-- every write; joining the type the membership already names costs one less scan.
CREATE OR REPLACE FUNCTION enforce_single_group_membership() RETURNS TRIGGER AS $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM resource_types rt
         WHERE rt.id = NEW.resource_type_id AND rt.single_group_membership
    ) THEN
        RETURN NEW;
    END IF;

    IF EXISTS (
        SELECT 1
        FROM resource_group_members m
        JOIN resource_groups g ON g.id = m.resource_group_id
        WHERE m.resource_id = NEW.resource_id
          AND m.resource_group_id <> NEW.resource_group_id
          AND g.resource_type_id = NEW.resource_type_id
          -- On UPDATE, ignore the row currently being moved.
          AND (TG_OP <> 'UPDATE'
               OR m.resource_group_id <> OLD.resource_group_id
               OR m.resource_id <> OLD.resource_id)
    ) THEN
        RAISE EXCEPTION 'resource % is already a member of another group', NEW.resource_id
            USING ERRCODE = 'unique_violation';
    END IF;

    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_space_single_group ON resource_group_members;
DROP FUNCTION IF EXISTS enforce_space_single_group();

CREATE TRIGGER trg_single_group_membership
    BEFORE INSERT OR UPDATE ON resource_group_members
    FOR EACH ROW EXECUTE FUNCTION enforce_single_group_membership();

-- ── Search indexer reads one table ────────────────────────────────────────────
-- 1690's version queried spaces and person_profiles for every resource, so indexing a tool
-- ran two joins that could never match. One row now holds all of it.
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

-- The profile triggers went with their tables; `resources` and `resource_capabilities` are
-- the only writes left that can change how a resource looks.
CREATE OR REPLACE FUNCTION trg_refresh_search_resource() RETURNS TRIGGER AS $$
BEGIN
    IF TG_OP = 'DELETE' THEN
        IF TG_TABLE_NAME = 'resources' THEN
            DELETE FROM search_documents WHERE entity_type = 'resource' AND entity_id = OLD.id;
        ELSE
            PERFORM refresh_search_resource(OLD.resource_id);
        END IF;
        RETURN OLD;
    END IF;

    IF TG_TABLE_NAME = 'resources' THEN
        PERFORM refresh_search_resource(NEW.id);
    ELSE
        PERFORM refresh_search_resource(NEW.resource_id);
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- ── Drop the side tables ──────────────────────────────────────────────────────
-- Their triggers go with them; naming them first keeps the intent explicit rather than
-- relying on the DROP TABLE to sweep them up.
DROP TRIGGER IF EXISTS trg_search_resource_spaces ON public.spaces;
DROP TRIGGER IF EXISTS trg_search_resource_person_profiles ON public.person_profiles;

DROP TABLE IF EXISTS public.spaces;
DROP TABLE IF EXISTS public.person_profiles;

-- Reindex: the search documents written before this point took their site from
-- spaces.site_id and their email from person_profiles, both now read off resources.
SELECT refresh_search_resource(r.id) FROM resources r;

COMMIT;
