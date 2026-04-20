-- V026: Add performance indexes for common query patterns
-- Improves query performance for filtered lists and lookups

-- Add index on spaces.code for lookups by code
-- (site_id index already exists from V001)
CREATE INDEX IF NOT EXISTS idx_spaces_code ON spaces(code) WHERE code IS NOT NULL;

-- Add composite index on spaces for site+code lookups
CREATE INDEX IF NOT EXISTS idx_spaces_site_code ON spaces(site_id, code);

-- Add composite index on requests for time-based filtering
-- This helps with queries like "get all requests between dates"
CREATE INDEX IF NOT EXISTS idx_requests_time_status ON requests(start_ts, end_ts, status) 
    WHERE start_ts IS NOT NULL AND end_ts IS NOT NULL;

-- Add index on request_requirements for reverse lookups
-- (request_id and criterion_id indexes already exist from V014)
-- This composite index helps with "find all requests with specific criterion value" queries
CREATE INDEX IF NOT EXISTS idx_request_requirements_criterion_value ON request_requirements(criterion_id, value);

-- Comments
COMMENT ON INDEX idx_spaces_code IS 'Lookup spaces by code (filtered for non-null values)';
COMMENT ON INDEX idx_spaces_site_code IS 'Composite index for site+code lookups';
COMMENT ON INDEX idx_requests_time_status IS 'Composite index for time-range queries with status filter';
COMMENT ON INDEX idx_request_requirements_criterion_value IS 'Find requests by criterion value';
