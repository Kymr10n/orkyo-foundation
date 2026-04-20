-- V019: Fix requests table to allow nullable timestamps for unscheduled requests
-- This aligns the database schema with the original design intent where
-- requests can be created without a schedule (start_ts and end_ts are NULL)

-- Drop the existing constraint that doesn't allow NULLs
ALTER TABLE requests
DROP CONSTRAINT IF EXISTS valid_time_range;

-- Make start_ts and end_ts nullable
ALTER TABLE requests
ALTER COLUMN start_ts DROP NOT NULL,
ALTER COLUMN end_ts DROP NOT NULL;

-- Re-add the constraint to allow both NULL or both NOT NULL
ALTER TABLE requests
ADD CONSTRAINT valid_time_range CHECK (
    (start_ts IS NULL AND end_ts IS NULL) OR 
    (start_ts IS NOT NULL AND end_ts IS NOT NULL AND end_ts > start_ts)
);

COMMENT ON CONSTRAINT valid_time_range ON requests IS 
'Ensures start_ts and end_ts are either both NULL (unscheduled) or both NOT NULL with end_ts > start_ts (scheduled)';
