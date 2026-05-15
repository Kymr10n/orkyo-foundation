-- @migration-class: expand

-- off_time_resources: the PK is (off_time_id, resource_id), which only accelerates
-- lookups leading with off_time_id. Queries that filter by resource_id alone
-- (e.g. GetOffTimesByResourceAsync) need a dedicated index.
CREATE INDEX ix_off_time_resources_resource_id
    ON public.off_time_resources (resource_id);

-- resources: ResourceRepository.GetAllAsync filters by name ILIKE @search.
-- pg_trgm is already enabled (migration 1280). A GIN trigram index avoids the
-- sequential scan that ILIKE triggers without it.
CREATE INDEX ix_resources_name_trgm
    ON public.resources USING GIN (name gin_trgm_ops);
