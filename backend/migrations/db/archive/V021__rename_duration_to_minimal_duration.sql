-- Rename duration columns to minimal_duration in requests table
ALTER TABLE requests 
  RENAME COLUMN duration_value TO minimal_duration_value;

ALTER TABLE requests 
  RENAME COLUMN duration_unit TO minimal_duration_unit;

-- Rename duration columns to minimal_duration in request_templates table
ALTER TABLE request_templates 
  RENAME COLUMN duration_value TO minimal_duration_value;

ALTER TABLE request_templates 
  RENAME COLUMN duration_unit TO minimal_duration_unit;

-- Update check constraint names to reflect the rename
ALTER TABLE requests 
  DROP CONSTRAINT requests_duration_value_check;

ALTER TABLE requests 
  ADD CONSTRAINT requests_minimal_duration_value_check CHECK (minimal_duration_value > 0);

ALTER TABLE requests 
  DROP CONSTRAINT requests_duration_unit_check;

ALTER TABLE requests 
  ADD CONSTRAINT requests_minimal_duration_unit_check CHECK (
    minimal_duration_unit IN ('minutes', 'hours', 'days', 'weeks', 'months', 'years')
  );

ALTER TABLE request_templates 
  DROP CONSTRAINT request_templates_duration_value_check;

ALTER TABLE request_templates 
  ADD CONSTRAINT request_templates_minimal_duration_value_check CHECK (minimal_duration_value > 0);

ALTER TABLE request_templates 
  DROP CONSTRAINT request_templates_duration_unit_check;

ALTER TABLE request_templates 
  ADD CONSTRAINT request_templates_minimal_duration_unit_check CHECK (
    minimal_duration_unit IN ('minutes', 'hours', 'days', 'weeks', 'months', 'years')
  );

COMMENT ON COLUMN requests.minimal_duration_value IS 'Minimum duration value for the request';
COMMENT ON COLUMN requests.minimal_duration_unit IS 'Unit of the minimum duration (minutes, hours, days, weeks, months, years)';
COMMENT ON COLUMN request_templates.minimal_duration_value IS 'Default minimum duration value for requests created from this template';
COMMENT ON COLUMN request_templates.minimal_duration_unit IS 'Default unit of the minimum duration';
