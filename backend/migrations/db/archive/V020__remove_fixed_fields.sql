-- Remove fixed_start, fixed_end, fixed_duration columns from requests and request_templates tables
-- These scheduling constraint flags are no longer needed

-- Drop from requests table
ALTER TABLE requests DROP COLUMN IF EXISTS fixed_start;
ALTER TABLE requests DROP COLUMN IF EXISTS fixed_end;
ALTER TABLE requests DROP COLUMN IF EXISTS fixed_duration;

-- Drop from request_templates table
ALTER TABLE request_templates DROP COLUMN IF EXISTS fixed_start;
ALTER TABLE request_templates DROP COLUMN IF EXISTS fixed_end;
ALTER TABLE request_templates DROP COLUMN IF EXISTS fixed_duration;

COMMENT ON TABLE requests IS 'Scheduled and unscheduled requests in the tenant database';
COMMENT ON TABLE request_templates IS 'Request templates for creating requests with predefined settings';
