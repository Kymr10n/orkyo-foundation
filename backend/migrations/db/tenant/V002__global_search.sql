-- Global Search Infrastructure
-- Implements FTS + trigram search across key entities

-- Enable trigram extension (if not already enabled)
CREATE EXTENSION IF NOT EXISTS pg_trgm;

-- ============================================================================
-- Search Documents Table
-- ============================================================================
-- Unified projection table for full-text search across all searchable entities

CREATE TABLE public.search_documents (
    entity_type    TEXT NOT NULL,
    entity_id      UUID NOT NULL,
    site_id        UUID NULL,
    
    title          TEXT NOT NULL,
    subtitle       TEXT NULL,
    keywords       TEXT NULL,
    
    fts            TSVECTOR NOT NULL,
    
    updated_at     TIMESTAMPTZ NOT NULL DEFAULT now(),
    
    PRIMARY KEY (entity_type, entity_id)
);

COMMENT ON TABLE public.search_documents IS 'Unified search index for global fuzzy search across all entities';
COMMENT ON COLUMN public.search_documents.entity_type IS 'Type of entity: space, request, group, site, template, criterion';
COMMENT ON COLUMN public.search_documents.site_id IS 'Site scope for site-specific entities (spaces, requests); NULL for tenant-level entities';
COMMENT ON COLUMN public.search_documents.title IS 'Primary searchable text (entity name)';
COMMENT ON COLUMN public.search_documents.subtitle IS 'Secondary text (description, group name, etc.)';
COMMENT ON COLUMN public.search_documents.keywords IS 'Additional searchable terms (capabilities, status, etc.)';
COMMENT ON COLUMN public.search_documents.fts IS 'Pre-computed tsvector for full-text search';

-- ============================================================================
-- Indexes
-- ============================================================================

-- Filter by site (most queries will filter by site)
CREATE INDEX idx_search_documents_site ON public.search_documents (site_id);

-- Full-text search using GIN
CREATE INDEX idx_search_documents_fts ON public.search_documents USING GIN (fts);

-- Trigram indexes for fuzzy matching
CREATE INDEX idx_search_documents_title_trgm ON public.search_documents USING GIN (title gin_trgm_ops);
CREATE INDEX idx_search_documents_keywords_trgm ON public.search_documents USING GIN (keywords gin_trgm_ops);

-- ============================================================================
-- Helper Function: Build FTS Vector
-- ============================================================================

CREATE OR REPLACE FUNCTION build_search_fts(
    p_title TEXT,
    p_keywords TEXT,
    p_subtitle TEXT
) RETURNS TSVECTOR AS $$
BEGIN
    RETURN
        setweight(to_tsvector('simple', coalesce(p_title, '')), 'A') ||
        setweight(to_tsvector('simple', coalesce(p_keywords, '')), 'B') ||
        setweight(to_tsvector('simple', coalesce(p_subtitle, '')), 'C');
END;
$$ LANGUAGE plpgsql IMMUTABLE;

-- ============================================================================
-- Trigger Functions
-- ============================================================================

-- SPACES: index name, group name, code, capabilities
CREATE OR REPLACE FUNCTION sync_search_spaces() RETURNS TRIGGER AS $$
DECLARE
    v_group_name TEXT;
    v_capabilities TEXT;
BEGIN
    IF TG_OP = 'DELETE' THEN
        DELETE FROM search_documents WHERE entity_type = 'space' AND entity_id = OLD.id;
        RETURN OLD;
    END IF;
    
    -- Get group name
    SELECT name INTO v_group_name FROM space_groups WHERE id = NEW.group_id;
    
    -- Get capabilities as keywords
    SELECT string_agg(c.name, ' ')
    INTO v_capabilities
    FROM space_capabilities sc
    JOIN criteria c ON c.id = sc.criterion_id
    WHERE sc.space_id = NEW.id;
    
    INSERT INTO search_documents (entity_type, entity_id, site_id, title, subtitle, keywords, fts, updated_at)
    VALUES (
        'space',
        NEW.id,
        NEW.site_id,
        NEW.name,
        coalesce(v_group_name, '') || ' ' || coalesce(NEW.description, ''),
        coalesce(NEW.code, '') || ' ' || coalesce(v_capabilities, ''),
        build_search_fts(
            NEW.name,
            coalesce(NEW.code, '') || ' ' || coalesce(v_capabilities, ''),
            coalesce(v_group_name, '') || ' ' || coalesce(NEW.description, '')
        ),
        now()
    )
    ON CONFLICT (entity_type, entity_id) DO UPDATE SET
        site_id = EXCLUDED.site_id,
        title = EXCLUDED.title,
        subtitle = EXCLUDED.subtitle,
        keywords = EXCLUDED.keywords,
        fts = EXCLUDED.fts,
        updated_at = EXCLUDED.updated_at;
    
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- REQUESTS: index name, description, status, space name
CREATE OR REPLACE FUNCTION sync_search_requests() RETURNS TRIGGER AS $$
DECLARE
    v_space_name TEXT;
    v_site_id UUID;
BEGIN
    IF TG_OP = 'DELETE' THEN
        DELETE FROM search_documents WHERE entity_type = 'request' AND entity_id = OLD.id;
        RETURN OLD;
    END IF;
    
    -- Get space info
    SELECT s.name, s.site_id INTO v_space_name, v_site_id
    FROM spaces s WHERE s.id = NEW.space_id;
    
    INSERT INTO search_documents (entity_type, entity_id, site_id, title, subtitle, keywords, fts, updated_at)
    VALUES (
        'request',
        NEW.id,
        v_site_id,
        NEW.name,
        coalesce(NEW.description, ''),
        coalesce(NEW.status, '') || ' ' || coalesce(v_space_name, '') || ' ' || coalesce(NEW.request_item_id, ''),
        build_search_fts(
            NEW.name,
            coalesce(NEW.status, '') || ' ' || coalesce(v_space_name, '') || ' ' || coalesce(NEW.request_item_id, ''),
            coalesce(NEW.description, '')
        ),
        now()
    )
    ON CONFLICT (entity_type, entity_id) DO UPDATE SET
        site_id = EXCLUDED.site_id,
        title = EXCLUDED.title,
        subtitle = EXCLUDED.subtitle,
        keywords = EXCLUDED.keywords,
        fts = EXCLUDED.fts,
        updated_at = EXCLUDED.updated_at;
    
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- GROUPS: index name, description, capabilities
CREATE OR REPLACE FUNCTION sync_search_groups() RETURNS TRIGGER AS $$
DECLARE
    v_capabilities TEXT;
BEGIN
    IF TG_OP = 'DELETE' THEN
        DELETE FROM search_documents WHERE entity_type = 'group' AND entity_id = OLD.id;
        RETURN OLD;
    END IF;
    
    -- Get capabilities as keywords
    SELECT string_agg(c.name, ' ')
    INTO v_capabilities
    FROM group_capabilities gc
    JOIN criteria c ON c.id = gc.criterion_id
    WHERE gc.group_id = NEW.id;
    
    INSERT INTO search_documents (entity_type, entity_id, site_id, title, subtitle, keywords, fts, updated_at)
    VALUES (
        'group',
        NEW.id,
        NULL,  -- Groups are tenant-level, not site-specific
        NEW.name,
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
        title = EXCLUDED.title,
        subtitle = EXCLUDED.subtitle,
        keywords = EXCLUDED.keywords,
        fts = EXCLUDED.fts,
        updated_at = EXCLUDED.updated_at;
    
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- SITES: index name, code, description
CREATE OR REPLACE FUNCTION sync_search_sites() RETURNS TRIGGER AS $$
BEGIN
    IF TG_OP = 'DELETE' THEN
        DELETE FROM search_documents WHERE entity_type = 'site' AND entity_id = OLD.id;
        RETURN OLD;
    END IF;
    
    INSERT INTO search_documents (entity_type, entity_id, site_id, title, subtitle, keywords, fts, updated_at)
    VALUES (
        'site',
        NEW.id,
        NEW.id,  -- Site's own ID for consistency in filtering
        NEW.name,
        coalesce(NEW.description, ''),
        coalesce(NEW.code, '') || ' ' || coalesce(NEW.address, ''),
        build_search_fts(
            NEW.name,
            coalesce(NEW.code, '') || ' ' || coalesce(NEW.address, ''),
            coalesce(NEW.description, '')
        ),
        now()
    )
    ON CONFLICT (entity_type, entity_id) DO UPDATE SET
        title = EXCLUDED.title,
        subtitle = EXCLUDED.subtitle,
        keywords = EXCLUDED.keywords,
        fts = EXCLUDED.fts,
        updated_at = EXCLUDED.updated_at;
    
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- TEMPLATES: index name, description, entity_type
CREATE OR REPLACE FUNCTION sync_search_templates() RETURNS TRIGGER AS $$
BEGIN
    IF TG_OP = 'DELETE' THEN
        DELETE FROM search_documents WHERE entity_type = 'template' AND entity_id = OLD.id;
        RETURN OLD;
    END IF;
    
    INSERT INTO search_documents (entity_type, entity_id, site_id, title, subtitle, keywords, fts, updated_at)
    VALUES (
        'template',
        NEW.id,
        NULL,  -- Templates are tenant-level
        NEW.name,
        coalesce(NEW.description, ''),
        coalesce(NEW.entity_type, ''),
        build_search_fts(
            NEW.name,
            coalesce(NEW.entity_type, ''),
            coalesce(NEW.description, '')
        ),
        now()
    )
    ON CONFLICT (entity_type, entity_id) DO UPDATE SET
        title = EXCLUDED.title,
        subtitle = EXCLUDED.subtitle,
        keywords = EXCLUDED.keywords,
        fts = EXCLUDED.fts,
        updated_at = EXCLUDED.updated_at;
    
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- CRITERIA: index name, description, data_type
CREATE OR REPLACE FUNCTION sync_search_criteria() RETURNS TRIGGER AS $$
BEGIN
    IF TG_OP = 'DELETE' THEN
        DELETE FROM search_documents WHERE entity_type = 'criterion' AND entity_id = OLD.id;
        RETURN OLD;
    END IF;
    
    INSERT INTO search_documents (entity_type, entity_id, site_id, title, subtitle, keywords, fts, updated_at)
    VALUES (
        'criterion',
        NEW.id,
        NULL,  -- Criteria are tenant-level
        NEW.name,
        coalesce(NEW.description, ''),
        coalesce(NEW.data_type, '') || ' ' || coalesce(NEW.unit, ''),
        build_search_fts(
            NEW.name,
            coalesce(NEW.data_type, '') || ' ' || coalesce(NEW.unit, ''),
            coalesce(NEW.description, '')
        ),
        now()
    )
    ON CONFLICT (entity_type, entity_id) DO UPDATE SET
        title = EXCLUDED.title,
        subtitle = EXCLUDED.subtitle,
        keywords = EXCLUDED.keywords,
        fts = EXCLUDED.fts,
        updated_at = EXCLUDED.updated_at;
    
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- ============================================================================
-- Create Triggers
-- ============================================================================

CREATE TRIGGER trg_search_spaces
    AFTER INSERT OR UPDATE OR DELETE ON spaces
    FOR EACH ROW EXECUTE FUNCTION sync_search_spaces();

CREATE TRIGGER trg_search_requests
    AFTER INSERT OR UPDATE OR DELETE ON requests
    FOR EACH ROW EXECUTE FUNCTION sync_search_requests();

CREATE TRIGGER trg_search_groups
    AFTER INSERT OR UPDATE OR DELETE ON space_groups
    FOR EACH ROW EXECUTE FUNCTION sync_search_groups();

CREATE TRIGGER trg_search_sites
    AFTER INSERT OR UPDATE OR DELETE ON sites
    FOR EACH ROW EXECUTE FUNCTION sync_search_sites();

CREATE TRIGGER trg_search_templates
    AFTER INSERT OR UPDATE OR DELETE ON templates
    FOR EACH ROW EXECUTE FUNCTION sync_search_templates();

CREATE TRIGGER trg_search_criteria
    AFTER INSERT OR UPDATE OR DELETE ON criteria
    FOR EACH ROW EXECUTE FUNCTION sync_search_criteria();

-- ============================================================================
-- Backfill Existing Data
-- ============================================================================
-- Populate search_documents with existing entities

-- Backfill spaces
INSERT INTO search_documents (entity_type, entity_id, site_id, title, subtitle, keywords, fts, updated_at)
SELECT
    'space',
    s.id,
    s.site_id,
    s.name,
    coalesce(sg.name, '') || ' ' || coalesce(s.description, ''),
    coalesce(s.code, '') || ' ' || coalesce(
        (SELECT string_agg(c.name, ' ') FROM space_capabilities sc JOIN criteria c ON c.id = sc.criterion_id WHERE sc.space_id = s.id),
        ''
    ),
    build_search_fts(
        s.name,
        coalesce(s.code, '') || ' ' || coalesce(
            (SELECT string_agg(c.name, ' ') FROM space_capabilities sc JOIN criteria c ON c.id = sc.criterion_id WHERE sc.space_id = s.id),
            ''
        ),
        coalesce(sg.name, '') || ' ' || coalesce(s.description, '')
    ),
    now()
FROM spaces s
LEFT JOIN space_groups sg ON sg.id = s.group_id
ON CONFLICT (entity_type, entity_id) DO NOTHING;

-- Backfill requests
INSERT INTO search_documents (entity_type, entity_id, site_id, title, subtitle, keywords, fts, updated_at)
SELECT
    'request',
    r.id,
    sp.site_id,
    r.name,
    coalesce(r.description, ''),
    coalesce(r.status, '') || ' ' || coalesce(sp.name, '') || ' ' || coalesce(r.request_item_id, ''),
    build_search_fts(
        r.name,
        coalesce(r.status, '') || ' ' || coalesce(sp.name, '') || ' ' || coalesce(r.request_item_id, ''),
        coalesce(r.description, '')
    ),
    now()
FROM requests r
LEFT JOIN spaces sp ON sp.id = r.space_id
ON CONFLICT (entity_type, entity_id) DO NOTHING;

-- Backfill groups
INSERT INTO search_documents (entity_type, entity_id, site_id, title, subtitle, keywords, fts, updated_at)
SELECT
    'group',
    g.id,
    NULL,
    g.name,
    coalesce(g.description, ''),
    coalesce(
        (SELECT string_agg(c.name, ' ') FROM group_capabilities gc JOIN criteria c ON c.id = gc.criterion_id WHERE gc.group_id = g.id),
        ''
    ),
    build_search_fts(
        g.name,
        coalesce(
            (SELECT string_agg(c.name, ' ') FROM group_capabilities gc JOIN criteria c ON c.id = gc.criterion_id WHERE gc.group_id = g.id),
            ''
        ),
        coalesce(g.description, '')
    ),
    now()
FROM space_groups g
ON CONFLICT (entity_type, entity_id) DO NOTHING;

-- Backfill sites
INSERT INTO search_documents (entity_type, entity_id, site_id, title, subtitle, keywords, fts, updated_at)
SELECT
    'site',
    id,
    id,
    name,
    coalesce(description, ''),
    coalesce(code, '') || ' ' || coalesce(address, ''),
    build_search_fts(
        name,
        coalesce(code, '') || ' ' || coalesce(address, ''),
        coalesce(description, '')
    ),
    now()
FROM sites
ON CONFLICT (entity_type, entity_id) DO NOTHING;

-- Backfill templates
INSERT INTO search_documents (entity_type, entity_id, site_id, title, subtitle, keywords, fts, updated_at)
SELECT
    'template',
    id,
    NULL,
    name,
    coalesce(description, ''),
    coalesce(entity_type, ''),
    build_search_fts(
        name,
        coalesce(entity_type, ''),
        coalesce(description, '')
    ),
    now()
FROM templates
ON CONFLICT (entity_type, entity_id) DO NOTHING;

-- Backfill criteria
INSERT INTO search_documents (entity_type, entity_id, site_id, title, subtitle, keywords, fts, updated_at)
SELECT
    'criterion',
    id,
    NULL,
    name,
    coalesce(description, ''),
    coalesce(data_type, '') || ' ' || coalesce(unit, ''),
    build_search_fts(
        name,
        coalesce(data_type, '') || ' ' || coalesce(unit, ''),
        coalesce(description, '')
    ),
    now()
FROM criteria
ON CONFLICT (entity_type, entity_id) DO NOTHING;
