-- @migration-class: expand

-- Renaming a shared list row updates the search index again.
--
-- 1830 rebuilt `refresh_search_resource` to resolve organization-list labels, so a person is
-- found by their department and job title. It also rebuilt `trg_search_resources_update` to watch
-- `resources.custom_fields`, which catches a person being reassigned. Nothing watches the rows
-- themselves.
--
-- The gap: rename "Fitter" to "Technician" and every person still reads "Fitter" in the index,
-- indefinitely — searching the new name finds nobody. Deleting the row already refreshes, because
-- the delete writes `resources.custom_fields` and trips the existing trigger; a rename touches no
-- resource at all. The old `job_titles` table had the same hole, and 1830 claimed the replacement
-- finds strictly more than the old version did. This is what makes that true.
--
-- Scoped by the GIN index 1840 added: the lookup is `custom_fields -> key @> [row id]`, which is
-- exactly the shape that index serves, so a rename costs one index scan and one refresh per
-- resource that actually holds the row.

BEGIN;

CREATE OR REPLACE FUNCTION trg_refresh_search_list_row() RETURNS TRIGGER AS $$
DECLARE
    v_resource_id UUID;
BEGIN
    -- Only a value change matters. A row touched without its cells changing (a no-op save) would
    -- otherwise refresh every resource pointing at it for nothing.
    IF OLD.values IS NOT DISTINCT FROM NEW.values THEN
        RETURN NULL;
    END IF;

    FOR v_resource_id IN
        SELECT DISTINCT r.id
          FROM resources r
          JOIN resource_custom_fields f
            ON f.resource_type_id = r.resource_type_id
           AND f.data_type = 'list_lookup'
           AND f.list_instance_id = NEW.list_instance_id
         WHERE jsonb_typeof(r.custom_fields -> f.key) = 'array'
           AND r.custom_fields -> f.key @> to_jsonb(NEW.id::text)
    LOOP
        PERFORM refresh_search_resource(v_resource_id);
    END LOOP;

    RETURN NULL;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_search_list_rows_update ON public.list_rows;
CREATE TRIGGER trg_search_list_rows_update
    AFTER UPDATE ON public.list_rows
    FOR EACH ROW
    EXECUTE FUNCTION trg_refresh_search_list_row();

COMMIT;
