-- Reverts 1780. Every list definition, instance and row is discarded, along with the
-- bindings that pointed at them; the data is not recoverable.
--
-- Order matters: the binding columns on resource_custom_fields reference list_definitions
-- and list_instances with ON DELETE RESTRICT, so they go first. The four list tables then
-- drop cleanly — their own references are internal, and CASCADE on the resource/field FKs
-- points inward, not out.
--
-- The data_type CHECK is restored to 1770's five scalar types. Any field created as 'list'
-- or 'list_lookup' would violate it, so those rows are removed first: without the tables
-- they have nothing to point at, and a field that cannot resolve its shape is not a field.

BEGIN;

ALTER TABLE public.resource_custom_fields
    DROP CONSTRAINT IF EXISTS resource_custom_fields_list_binding_check;

ALTER TABLE public.resource_custom_fields
    DROP CONSTRAINT IF EXISTS resource_custom_fields_list_definition_fkey,
    DROP CONSTRAINT IF EXISTS resource_custom_fields_list_instance_fkey;

-- Values stored against lookup fields go with them, mirroring how 1770's revert discards
-- the values captured against the definitions it drops.
UPDATE public.resources r
    SET custom_fields = r.custom_fields - f.key
    FROM public.resource_custom_fields f
    WHERE f.resource_type_id = r.resource_type_id
      AND f.data_type IN ('list', 'list_lookup')
      AND jsonb_exists(r.custom_fields, f.key);

DELETE FROM public.resource_custom_fields WHERE data_type IN ('list', 'list_lookup');

ALTER TABLE public.resource_custom_fields
    DROP COLUMN IF EXISTS list_definition_id,
    DROP COLUMN IF EXISTS list_instance_id;

ALTER TABLE public.resource_custom_fields
    DROP CONSTRAINT IF EXISTS resource_custom_fields_data_type_check;

ALTER TABLE public.resource_custom_fields
    ADD CONSTRAINT resource_custom_fields_data_type_check
        CHECK (data_type IN ('text', 'number', 'boolean', 'date', 'url'));

DROP TABLE IF EXISTS public.list_rows;
DROP TABLE IF EXISTS public.list_instances;
DROP TABLE IF EXISTS public.list_columns;
DROP TABLE IF EXISTS public.list_definitions;

COMMIT;
