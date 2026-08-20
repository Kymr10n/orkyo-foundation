-- Reverts 1900. The department Parent goes back to being a name in a text cell.
--
-- Reversible in full: every id names a row that is still there, so it is rewritten as that row's
-- name — the shape 1820 stored. What 1900 dropped (a parent matching no department, or two) it
-- dropped for good; no revert can invent it back.

BEGIN;

DO $$
DECLARE
    dept_def UUID;
BEGIN
    SELECT id INTO dept_def
    FROM public.list_definitions
    WHERE name = 'Departments' AND scope = 'organization';

    IF dept_def IS NULL THEN RETURN; END IF;

    -- Ids that still resolve become names again.
    UPDATE public.list_rows r
       SET values = jsonb_set(r.values, '{parent}', to_jsonb(p.values ->> 'name'))
      FROM public.list_instances i, public.list_rows p
     WHERE i.id = r.list_instance_id
       AND i.list_definition_id = dept_def
       AND r.values ? 'parent'
       AND p.id = (r.values ->> 'parent')::uuid
       AND p.values ->> 'name' IS NOT NULL;

    -- Anything left is an id naming no row, which as text would read as a UUID nobody typed.
    UPDATE public.list_rows r
       SET values = r.values - 'parent'
      FROM public.list_instances i
     WHERE i.id = r.list_instance_id
       AND i.list_definition_id = dept_def
       AND r.values ? 'parent'
       AND (r.values ->> 'parent') ~ '^[0-9a-fA-F-]{36}$';

    UPDATE public.list_columns
       SET data_type = 'text',
           description = 'The unit this one sat under before departments became a list.'
     WHERE list_definition_id = dept_def
       AND key = 'parent'
       AND data_type = 'row_ref';
END $$;

COMMIT;
