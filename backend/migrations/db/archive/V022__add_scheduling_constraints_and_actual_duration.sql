-- V022: Add scheduling constraints and actual duration to requests
-- This migration adds fields for:
-- 1. Scheduling constraints (earliest_start_ts, latest_end_ts)
-- 2. Actual scheduled duration (actual_duration_value, actual_duration_unit)
-- These allow requests to become utilization when fully scheduled

-- Add scheduling constraint columns
ALTER TABLE requests 
ADD COLUMN earliest_start_ts TIMESTAMP WITH TIME ZONE,
ADD COLUMN latest_end_ts TIMESTAMP WITH TIME ZONE;

-- Add actual duration columns (set when scheduled)
ALTER TABLE requests
ADD COLUMN actual_duration_value INTEGER,
ADD COLUMN actual_duration_unit VARCHAR(20);

-- Add check constraint for actual duration unit
ALTER TABLE requests
ADD CONSTRAINT requests_actual_duration_unit_check 
CHECK (actual_duration_unit IS NULL OR actual_duration_unit IN ('minutes', 'hours', 'days', 'weeks', 'months', 'years'));

-- Add check constraint for actual duration value (must be positive if set)
ALTER TABLE requests
ADD CONSTRAINT requests_actual_duration_value_check 
CHECK (actual_duration_value IS NULL OR actual_duration_value > 0);

-- Add check constraint: if actual_duration is set, both value and unit must be set
ALTER TABLE requests
ADD CONSTRAINT requests_actual_duration_complete_check 
CHECK (
    (actual_duration_value IS NULL AND actual_duration_unit IS NULL) OR
    (actual_duration_value IS NOT NULL AND actual_duration_unit IS NOT NULL)
);

-- Add check constraint: earliest_start must be before latest_end if both are set
ALTER TABLE requests
ADD CONSTRAINT requests_constraint_dates_order_check 
CHECK (
    earliest_start_ts IS NULL OR 
    latest_end_ts IS NULL OR 
    earliest_start_ts < latest_end_ts
);

-- Add check constraint: if scheduled, start_ts must be within constraints
ALTER TABLE requests
ADD CONSTRAINT requests_scheduled_within_constraints_check 
CHECK (
    start_ts IS NULL OR 
    earliest_start_ts IS NULL OR 
    start_ts >= earliest_start_ts
);

-- Add check constraint: if scheduled, end_ts must be within constraints
ALTER TABLE requests
ADD CONSTRAINT requests_scheduled_end_within_constraints_check 
CHECK (
    end_ts IS NULL OR 
    latest_end_ts IS NULL OR 
    end_ts <= latest_end_ts
);

-- Add comments for documentation
COMMENT ON COLUMN requests.earliest_start_ts IS 'Earliest date/time this request can start (scheduling constraint)';
COMMENT ON COLUMN requests.latest_end_ts IS 'Latest date/time this request must end by (scheduling constraint)';
COMMENT ON COLUMN requests.actual_duration_value IS 'Actual scheduled duration value (set when request is scheduled)';
COMMENT ON COLUMN requests.actual_duration_unit IS 'Unit for actual scheduled duration (minutes, hours, days, weeks, months, years)';
