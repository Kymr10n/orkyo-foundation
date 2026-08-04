-- @migration-class: contract

-- Makes search cover every resource type instead of two hard-coded ones.
--
-- Indexing was per-entity: sync_search_spaces() on the spaces profile table wrote
-- entity_type='space', and sync_search_people() on resources early-returned unless the row
-- was a person. So `tool` — a seeded system type since 1300 — has never been searchable, and
-- neither has any type a tenant defines. Adding a type meant writing another trigger.
--
-- One function keyed on a resource id replaces both, with thin triggers on every table that
-- can change what a resource looks like in the palette. entity_type becomes 'resource' for
-- all of them and the type moves to a facet column, so the vocabulary no longer grows with
-- the data.
--
-- Contract, not expand: the 'space' and 'person' rows are superseded and must be deleted in
-- the same transaction. search_documents is PRIMARY KEY (entity_type, entity_id), so leaving
-- them would list every space and person twice in the command palette — once stale, forever,
-- since nothing writes those entity_types again.
--
-- The facet column itself is added by 1685 (expand), because the new code reads it before
-- this file runs.
--
-- Rollback: recreate sync_search_spaces/sync_search_people and their triggers from 1530/1510,
-- drop the resource trigger set, then re-run those files' backfills.

BEGIN;

-- ── One indexer for every resource ────────────────────────────────────────────
-- Takes a resource id rather than a trigger row, so each of the four triggers below can
-- resolve its own id and share this body. Everything that made a space or a person findable
-- is preserved: the space's code, the person's email and job title, and the names of the
-- criteria assigned to either.
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
    SELECT r.name, r.description, rt.key, r.home_site_id
      INTO v_name, v_description, v_type_key, v_site_id
      FROM resources r
      JOIN resource_types rt ON rt.id = r.resource_type_id
     WHERE r.id = p_resource_id;

    -- Gone between the trigger firing and this running, or never existed.
    IF v_name IS NULL THEN
        DELETE FROM search_documents
         WHERE entity_type = 'resource' AND entity_id = p_resource_id;
        RETURN;
    END IF;

    -- Space profile: its site is authoritative over the resource's home site, and its code
    -- is how people actually search for it ("A-14").
    SELECT s.site_id, s.code INTO v_site_id, v_code
      FROM spaces s WHERE s.id = p_resource_id;
    IF v_site_id IS NULL THEN
        SELECT r.home_site_id INTO v_site_id FROM resources r WHERE r.id = p_resource_id;
    END IF;

    SELECT pp.email, jt.name INTO v_email, v_job_title
      FROM person_profiles pp
      LEFT JOIN job_titles jt ON jt.id = pp.job_title_id
     WHERE pp.resource_id = p_resource_id;

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

-- ── Thin triggers ─────────────────────────────────────────────────────────────
-- Four tables can change how a resource appears. The old space trigger fired only on the
-- spaces profile table, so renaming a space (which writes resources.name) never re-indexed
-- it — a stale title until the next code change. Covering resources fixes that.
CREATE OR REPLACE FUNCTION trg_refresh_search_resource() RETURNS TRIGGER AS $$
BEGIN
    IF TG_OP = 'DELETE' THEN
        -- The resources row itself going away is the only true deletion; losing a profile
        -- or a capability just changes what the still-present resource looks like.
        IF TG_TABLE_NAME = 'resources' THEN
            DELETE FROM search_documents WHERE entity_type = 'resource' AND entity_id = OLD.id;
        ELSIF TG_TABLE_NAME = 'spaces' THEN
            PERFORM refresh_search_resource(OLD.id);
        ELSE
            PERFORM refresh_search_resource(OLD.resource_id);
        END IF;
        RETURN OLD;
    END IF;

    IF TG_TABLE_NAME IN ('resources', 'spaces') THEN
        PERFORM refresh_search_resource(NEW.id);
    ELSE
        PERFORM refresh_search_resource(NEW.resource_id);
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_search_spaces ON public.spaces;
DROP TRIGGER IF EXISTS sync_search_resources_people ON public.resources;
DROP TRIGGER IF EXISTS sync_search_person_profiles ON public.person_profiles;
DROP FUNCTION IF EXISTS sync_search_spaces();
DROP FUNCTION IF EXISTS sync_search_people();

CREATE TRIGGER trg_search_resources
    AFTER INSERT OR UPDATE OR DELETE ON public.resources
    FOR EACH ROW EXECUTE FUNCTION trg_refresh_search_resource();

CREATE TRIGGER trg_search_resource_spaces
    AFTER INSERT OR UPDATE OR DELETE ON public.spaces
    FOR EACH ROW EXECUTE FUNCTION trg_refresh_search_resource();

CREATE TRIGGER trg_search_resource_person_profiles
    AFTER INSERT OR UPDATE OR DELETE ON public.person_profiles
    FOR EACH ROW EXECUTE FUNCTION trg_refresh_search_resource();

CREATE TRIGGER trg_search_resource_capabilities
    AFTER INSERT OR UPDATE OR DELETE ON public.resource_capabilities
    FOR EACH ROW EXECUTE FUNCTION trg_refresh_search_resource();

-- ── Replace the superseded documents ──────────────────────────────────────────
-- The old vocabulary goes; no backfill here. 1710 rewrites this function to read the folded
-- columns and reindexes every resource as its last statement, so indexing now would be work
-- thrown away two migrations later — and the intervening 1700 backfill would fire the trigger
-- for every row on top of it. The gap between the two is a single deploy step.
DELETE FROM search_documents WHERE entity_type IN ('space', 'person');

COMMIT;
