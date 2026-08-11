-- Reverts 1770. Definitions and the values captured against them are both discarded;
-- nothing else references either, so no other table needs repair.

DROP TABLE IF EXISTS public.resource_custom_fields;

ALTER TABLE public.resources
    DROP COLUMN IF EXISTS custom_fields;
