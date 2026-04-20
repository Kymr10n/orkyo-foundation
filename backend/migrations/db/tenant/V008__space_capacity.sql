-- V008: Add space capacity for concurrent allocation limits (#76)

ALTER TABLE spaces ADD COLUMN capacity INT NOT NULL DEFAULT 1;

-- Backfill from properties JSONB where capacity was stored informally
UPDATE spaces
SET capacity = (properties->>'capacity')::int
WHERE properties->>'capacity' IS NOT NULL
  AND (properties->>'capacity')::int > 0;
