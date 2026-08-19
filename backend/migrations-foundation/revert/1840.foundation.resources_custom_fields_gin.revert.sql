-- Reverts 1840. The queries keep working without the index; they scan instead.

DROP INDEX CONCURRENTLY IF EXISTS public.idx_resources_custom_fields_gin;
