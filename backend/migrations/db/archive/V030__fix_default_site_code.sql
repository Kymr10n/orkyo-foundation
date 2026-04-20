-- Fix default site that was created without a code
-- The code is required for updates but was not set during initial insertion

UPDATE sites
SET code = 'DEFAULT'
WHERE code IS NULL
  AND name = 'Default Site';

-- Also set code for any other sites missing a code (use slugified name)
UPDATE sites
SET code = UPPER(REGEXP_REPLACE(LEFT(name, 50), '[^a-zA-Z0-9]+', '_', 'g'))
WHERE code IS NULL;

-- Ensure no duplicate codes by appending a suffix if needed
DO $$
DECLARE
    dup_code VARCHAR(63);
    site_rec RECORD;
    counter INT;
BEGIN
    -- Find any duplicate codes
    FOR dup_code IN
        SELECT code FROM sites GROUP BY code HAVING COUNT(*) > 1
    LOOP
        counter := 1;
        FOR site_rec IN
            SELECT id, code FROM sites WHERE code = dup_code ORDER BY created_at OFFSET 1
        LOOP
            UPDATE sites SET code = site_rec.code || '_' || counter WHERE id = site_rec.id;
            counter := counter + 1;
        END LOOP;
    END LOOP;
END $$;

-- Now make the code column NOT NULL to prevent future issues
ALTER TABLE sites ALTER COLUMN code SET NOT NULL;
