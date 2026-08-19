-- Reverts 1850. Restores the 1330 function body (site_id = NULL) and clears the
-- backfilled sites. The old application code never read the column for requests,
-- so this only returns search to its pre-1850 behaviour.

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

UPDATE public.search_documents
SET site_id = NULL
WHERE entity_type = 'request' AND site_id IS NOT NULL;
