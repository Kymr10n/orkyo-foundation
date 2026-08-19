-- @migration-class: data

-- Request search documents get their site back.
--
-- 1330 rebuilt sync_search_requests() after the resource cutover and wrote
-- site_id = NULL, because at that time a request had no site of its own (the
-- site lived on resource_assignments). 1550 then added requests.site_id, but
-- the trigger function was never rebuilt: every request document since carries
-- site_id = NULL, so a site-filtered search silently excludes all requests and
-- idx_search_documents_site never serves them.
--
-- Two parts: replace the function so new writes carry the site, then backfill
-- the documents that already exist. The trigger itself (trg_search_requests,
-- 1280) fires on every INSERT/UPDATE/DELETE with no column list, so a site
-- change refreshes the document without further work.

CREATE OR REPLACE FUNCTION sync_search_requests() RETURNS TRIGGER AS $$
BEGIN
    IF TG_OP = 'DELETE' THEN
        DELETE FROM search_documents WHERE entity_type = 'request' AND entity_id = OLD.id;
        RETURN OLD;
    END IF;

    INSERT INTO search_documents (entity_type, entity_id, site_id, title, subtitle, keywords, fts, updated_at)
    VALUES (
        'request', NEW.id, NEW.site_id, NEW.name,
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

UPDATE public.search_documents sd
SET site_id = r.site_id
FROM public.requests r
WHERE sd.entity_type = 'request'
  AND sd.entity_id = r.id
  AND sd.site_id IS DISTINCT FROM r.site_id;
