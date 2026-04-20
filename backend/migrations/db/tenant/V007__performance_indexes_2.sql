-- Performance Audit 2: additional indexes

-- #16: Case-insensitive unique constraint on criteria name for ON CONFLICT
CREATE UNIQUE INDEX IF NOT EXISTS idx_criteria_name_lower
    ON criteria (LOWER(name));

-- #21: Supersedes V006 idx_requests_scheduling — adds start_ts to cover the full
-- JOIN + WHERE pattern in scheduling recalculation queries.
-- V006's idx_requests_scheduling is a prefix subset and becomes redundant.
DROP INDEX IF EXISTS idx_requests_scheduling;
CREATE INDEX IF NOT EXISTS idx_requests_scheduling_join
    ON requests (space_id, scheduling_settings_apply, start_ts)
    WHERE scheduling_settings_apply = true AND start_ts IS NOT NULL;

-- Sort index for sites pagination (ORDER BY name)
CREATE INDEX IF NOT EXISTS idx_sites_name
    ON sites (name);

-- Composite index for search_documents site + entity_type filter
CREATE INDEX IF NOT EXISTS idx_search_documents_site_type
    ON search_documents (site_id, entity_type);
