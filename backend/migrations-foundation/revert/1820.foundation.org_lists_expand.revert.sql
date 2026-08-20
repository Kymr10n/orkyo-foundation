-- Reverts 1820. The two lists go away and the tables become the truth again.
--
-- Only safe while 1830 has not run: this reads `resources.department_id` and `job_title_id` back
-- into being authoritative, and 1830 drops them. Reverting a contracted database means reverting
-- 1830 first, which restores the columns empty, and then this script has nothing to give back.
--
-- Edits made to the lists after 1820 are lost. A department added as a list row never existed in
-- `departments`, and this script does not invent it there — inventing rows in a table that the
-- application had stopped writing to is a data decision, not a revert.

BEGIN;

DO $$
DECLARE
    dept_instance UUID;
    job_instance  UUID;
BEGIN
    SELECT i.id INTO dept_instance
    FROM public.list_instances i
    JOIN public.list_definitions d ON d.id = i.list_definition_id
    WHERE d.name = 'Departments' AND d.scope = 'organization' AND i.kind = 'shared';

    SELECT i.id INTO job_instance
    FROM public.list_instances i
    JOIN public.list_definitions d ON d.id = i.list_definition_id
    WHERE d.name = 'Job Titles' AND d.scope = 'organization' AND i.kind = 'shared';

    -- The lookup fields first: list_instances is RESTRICTed while a field binds it.
    DELETE FROM public.resource_custom_fields
    WHERE data_type = 'list_lookup'
      AND list_instance_id IN (dept_instance, job_instance);

    UPDATE public.resources
    SET custom_fields = custom_fields - 'department' - 'job_title'
    WHERE custom_fields ?| ARRAY['department', 'job_title'];

    DELETE FROM public.list_rows WHERE list_instance_id IN (dept_instance, job_instance);
    DELETE FROM public.list_instances WHERE id IN (dept_instance, job_instance);
    DELETE FROM public.list_definitions
    WHERE scope = 'organization' AND name IN ('Departments', 'Job Titles');
END $$;

COMMIT;
