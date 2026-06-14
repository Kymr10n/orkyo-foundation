-- @migration-class: contract

-- Remove the stored operational "current site" from resources.
--
-- The Home-Site / Current-Site model (1550) stored current_site_id as a manually
-- maintained column. In practice it drifted from reality: assigning a person to
-- another site never updated it, so it had to be hand-edited and the edit was even
-- locked while the person had a schedule. Where a resource actually is at a point in
-- time is now DERIVED — spaces use spaces.site_id; people/tools are pinned by the
-- assignment covering that time and anchored to home_site_id when idle. home_site_id
-- and cross_site_allowed remain. See SchedulingRepository / ResourceAssignmentValidator.

BEGIN;

DROP INDEX IF EXISTS idx_resources_current_site;

-- The column's site_id FK was declared inline (1550), so DROP COLUMN removes it too.
ALTER TABLE resources
    DROP COLUMN IF EXISTS current_site_id;

COMMIT;
