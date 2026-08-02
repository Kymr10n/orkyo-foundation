-- @migration-class: expand

-- Adds the facet column the generic search indexer writes and the API reads.
--
-- Split out of 1690, which is classed `contract` because it deletes superseded documents.
-- A contract migration is sequenced to run after the code that stopped needing the old shape,
-- so a column the *new* code requires cannot live there: SearchRepository selects
-- resource_type_key unconditionally, and would meet a 42703 in the window between the code
-- deploy and 1690. Expand carries what the new code needs; contract carries what the old code
-- leaves behind.
--
-- Rollback: drop the column and the index.

BEGIN;

-- The type is a property of the row, not of the vocabulary. Nullable because only resource
-- documents have one; requests, sites, templates and criteria do not.
ALTER TABLE public.search_documents
    ADD COLUMN IF NOT EXISTS resource_type_key TEXT;

COMMENT ON COLUMN public.search_documents.resource_type_key IS
    'For entity_type=''resource'': the resource type key, so the client can route and label '
    'without a second query. NULL for every other entity type.';

COMMIT;

-- After COMMIT so it can be CONCURRENTLY: nothing in this migration needs the index, only the
-- type-filtered queries do. The runner does not wrap scripts, so statements after COMMIT run
-- in autocommit.
--
-- If a CONCURRENTLY build is interrupted it leaves an INVALID index that IF NOT EXISTS will
-- then skip forever. Check with:
--   SELECT indexrelid::regclass FROM pg_index WHERE NOT indisvalid;
-- and DROP INDEX CONCURRENTLY the invalid one before re-running.
CREATE INDEX CONCURRENTLY IF NOT EXISTS idx_search_documents_resource_type
    ON public.search_documents (resource_type_key)
    WHERE resource_type_key IS NOT NULL;
