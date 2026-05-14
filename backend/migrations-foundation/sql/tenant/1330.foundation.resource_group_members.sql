-- @migration-class: expand

-- ── Fix search trigger functions broken by migration 1310 ─────────────────────
--
-- Migration 1310 renamed tables/columns but did not update the search trigger
-- functions defined in migration 1280. All three functions below reference
-- symbols that no longer exist. This migration replaces them.

-- Fix 1: sync_search_requests — used NEW.space_id which was dropped.
-- site_id cannot be derived from the requests row alone (it lives in
-- resource_assignments). Set to NULL; documents are now tenant-scoped.
CREATE OR REPLACE FUNCTION sync_search_requests() RETURNS TRIGGER AS $$
BEGIN
    IF TG_OP = 'DELETE' THEN
        DELETE FROM search_documents WHERE entity_type = 'request' AND entity_id = OLD.id;
        RETURN OLD;
    END IF;

    INSERT INTO search_documents (entity_type, entity_id, site_id, title, subtitle, keywords, fts, updated_at)
    VALUES (
        'request', NEW.id, NULL, NEW.name,
        coalesce(NEW.description, ''),
        coalesce(NEW.status, '') || ' ' || coalesce(NEW.request_item_id, ''),
        build_search_fts(
            NEW.name,
            coalesce(NEW.status, '') || ' ' || coalesce(NEW.request_item_id, ''),
            coalesce(NEW.description, '')
        ),
        now()
    )
    ON CONFLICT (entity_type, entity_id) DO UPDATE SET
        site_id    = EXCLUDED.site_id,
        title      = EXCLUDED.title,
        subtitle   = EXCLUDED.subtitle,
        keywords   = EXCLUDED.keywords,
        fts        = EXCLUDED.fts,
        updated_at = EXCLUDED.updated_at;

    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- Fix 2: sync_search_spaces — used spaces.name/description (dropped),
-- space_groups (→ resource_groups), space_capabilities.space_id
-- (→ resource_capabilities.resource_id).
CREATE OR REPLACE FUNCTION sync_search_spaces() RETURNS TRIGGER AS $$
DECLARE
    v_name        TEXT;
    v_description TEXT;
    v_group_name  TEXT;
    v_capabilities TEXT;
BEGIN
    IF TG_OP = 'DELETE' THEN
        DELETE FROM search_documents WHERE entity_type = 'space' AND entity_id = OLD.id;
        RETURN OLD;
    END IF;

    -- spaces.id = resources.id (Phase 2 invariant); name/description moved there.
    SELECT r.name, r.description INTO v_name, v_description
    FROM resources r WHERE r.id = NEW.id;

    SELECT name INTO v_group_name
    FROM resource_groups WHERE id = NEW.group_id;

    SELECT string_agg(c.name, ' ') INTO v_capabilities
    FROM resource_capabilities rc
    JOIN criteria c ON c.id = rc.criterion_id
    WHERE rc.resource_id = NEW.id;

    INSERT INTO search_documents (entity_type, entity_id, site_id, title, subtitle, keywords, fts, updated_at)
    VALUES (
        'space', NEW.id, NEW.site_id, v_name,
        coalesce(v_group_name, '') || ' ' || coalesce(v_description, ''),
        coalesce(NEW.code, '') || ' ' || coalesce(v_capabilities, ''),
        build_search_fts(
            v_name,
            coalesce(NEW.code, '') || ' ' || coalesce(v_capabilities, ''),
            coalesce(v_group_name, '') || ' ' || coalesce(v_description, '')
        ),
        now()
    )
    ON CONFLICT (entity_type, entity_id) DO UPDATE SET
        site_id    = EXCLUDED.site_id,
        title      = EXCLUDED.title,
        subtitle   = EXCLUDED.subtitle,
        keywords   = EXCLUDED.keywords,
        fts        = EXCLUDED.fts,
        updated_at = EXCLUDED.updated_at;

    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- Fix 3: sync_search_groups — used group_capabilities.group_id
-- (→ resource_group_capabilities.resource_group_id).
CREATE OR REPLACE FUNCTION sync_search_groups() RETURNS TRIGGER AS $$
DECLARE
    v_capabilities TEXT;
BEGIN
    IF TG_OP = 'DELETE' THEN
        DELETE FROM search_documents WHERE entity_type = 'group' AND entity_id = OLD.id;
        RETURN OLD;
    END IF;

    SELECT string_agg(c.name, ' ') INTO v_capabilities
    FROM resource_group_capabilities gc
    JOIN criteria c ON c.id = gc.criterion_id
    WHERE gc.resource_group_id = NEW.id;

    INSERT INTO search_documents (entity_type, entity_id, site_id, title, subtitle, keywords, fts, updated_at)
    VALUES (
        'group', NEW.id, NULL, NEW.name,
        coalesce(NEW.description, ''),
        coalesce(v_capabilities, ''),
        build_search_fts(
            NEW.name,
            coalesce(v_capabilities, ''),
            coalesce(NEW.description, '')
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

-- ── Fix request_requirements missing columns (Phase 3 omission) ──────────────
-- Migration 1320 added applicable_to_requests to criteria but did not add
-- the operator and allowed_values columns to request_requirements, which
-- the Phase 3 backend code references.
ALTER TABLE request_requirements
    ADD COLUMN IF NOT EXISTS operator      TEXT,
    ADD COLUMN IF NOT EXISTS allowed_values JSONB;

-- ── criterion_resource_types: backfill and auto-populate for new criteria ─────
-- Migration 1300 only seeded 'space'. Backfill person and tool for all criteria
-- that already exist at migration time.
INSERT INTO criterion_resource_types (criterion_id, resource_type_id)
SELECT c.id, rt.id
FROM criteria c
CROSS JOIN resource_types rt
ON CONFLICT DO NOTHING;

-- Any criterion inserted after this migration (e.g. test seed data) should also
-- be applicable to all resource types automatically.
CREATE OR REPLACE FUNCTION auto_criterion_resource_types() RETURNS TRIGGER AS $$
BEGIN
    INSERT INTO criterion_resource_types (criterion_id, resource_type_id)
    SELECT NEW.id, id FROM resource_types
    ON CONFLICT DO NOTHING;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_auto_criterion_resource_types
    AFTER INSERT ON criteria
    FOR EACH ROW EXECUTE FUNCTION auto_criterion_resource_types();

-- ── Fix resource_groups.resource_type_id missing DEFAULT ─────────────────────
-- Migration 1310 added resource_type_id NOT NULL but without a DEFAULT.
-- SpaceGroupRepository.CreateAsync does not supply this column, so every
-- INSERT into resource_groups fails with a NOT NULL violation.
-- Since space groups are always of type 'space', that is the correct default.
CREATE OR REPLACE FUNCTION get_space_resource_type_id() RETURNS UUID AS $$
    SELECT id FROM resource_types WHERE key = 'space' LIMIT 1;
$$ LANGUAGE sql STABLE;

ALTER TABLE resource_groups
    ALTER COLUMN resource_type_id
    SET DEFAULT get_space_resource_type_id();

-- ── Fix resource_assignments unique index to match ON CONFLICT clause ─────────
-- RequestRepository.WriteResourceAssignmentAsync uses:
--   ON CONFLICT (request_id, resource_id) WHERE assignment_status != 'Cancelled'
-- This requires a PARTIAL unique index, but migration 1300 created a non-partial
-- index. Replace it with the correct partial index.
DROP INDEX IF EXISTS ux_ra_active_request_resource;
CREATE UNIQUE INDEX ux_ra_active_request_resource
    ON resource_assignments (request_id, resource_id)
    WHERE assignment_status != 'Cancelled';

-- ── resource_group_members ────────────────────────────────────────────────────
CREATE TABLE resource_group_members (
  resource_group_id UUID NOT NULL REFERENCES resource_groups(id) ON DELETE CASCADE,
  resource_id       UUID NOT NULL REFERENCES resources(id)       ON DELETE CASCADE,
  PRIMARY KEY (resource_group_id, resource_id)
);
