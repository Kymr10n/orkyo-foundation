-- @migration-class: contract

-- Completes the migration of space→group membership onto resource_group_members.
-- The legacy spaces.group_id column was left in place when membership moved to the
-- join table; the spaces read path and the scheduler grid still read it, so seeded
-- spaces (whose membership lives only in resource_group_members) showed as Ungrouped.
--
-- This migration:
--   1. Re-points the search trigger's group-name lookup at resource_group_members.
--   2. Drops the legacy spaces.group_id column (+ its index and FK).
--   3. Adds a space-scoped 1:1 guard: a space may belong to at most one group.
--      People remain potentially multi-group, so the guard is scoped to the
--      'space' resource type only.

BEGIN;

-- ── 1. Search trigger: group name now comes from resource_group_members ───────
-- Previously read NEW.group_id (about to be dropped). A space has at most one
-- group (enforced below), so LIMIT 1 is exact.
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

    SELECT g.name INTO v_group_name
    FROM resource_group_members m
    JOIN resource_groups g ON g.id = m.resource_group_id
    WHERE m.resource_id = NEW.id
    LIMIT 1;

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

-- ── 2. Drop the legacy column, its index and FK ───────────────────────────────
DROP INDEX IF EXISTS idx_spaces_group_id;

ALTER TABLE public.spaces
    DROP CONSTRAINT IF EXISTS spaces_group_id_fkey,
    DROP COLUMN IF EXISTS group_id;

-- ── 3. Space-scoped 1:1 membership guard ──────────────────────────────────────
-- A space (resource of type 'space') may be a member of at most one group.
-- Cannot be a partial unique index: the 'space' resource_type id is generated
-- per tenant, so the predicate isn't an immutable literal. A row trigger that
-- resolves the type at runtime is UUID-independent. Defense-in-depth behind the
-- app-layer move-semantics in ResourceGroupMemberRepository.SetMembersAsync.
CREATE OR REPLACE FUNCTION enforce_space_single_group() RETURNS TRIGGER AS $$
DECLARE
    v_space_type uuid;
BEGIN
    SELECT id INTO v_space_type FROM resource_types WHERE key = 'space';

    IF NEW.resource_type_id = v_space_type THEN
        IF EXISTS (
            SELECT 1
            FROM resource_group_members m
            JOIN resource_groups g ON g.id = m.resource_group_id
            WHERE m.resource_id = NEW.resource_id
              AND m.resource_group_id <> NEW.resource_group_id
              AND g.resource_type_id = v_space_type
        ) THEN
            RAISE EXCEPTION 'space % is already a member of another group', NEW.resource_id
                USING ERRCODE = 'unique_violation';
        END IF;
    END IF;

    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_space_single_group ON resource_group_members;
CREATE TRIGGER trg_space_single_group
    BEFORE INSERT ON resource_group_members
    FOR EACH ROW EXECUTE FUNCTION enforce_space_single_group();

COMMIT;
